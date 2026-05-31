using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.CLI.Application.Telemetry.Diagnostics;
using Sentinel.CLI.Application.Telemetry.Ports;
using Sentinel.CLI.Receiver.Telemetry;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Sentinel.CLI.Receiver;

public static class OtlpReceiverExtensions
{
    private const string ProtobufContentType = "application/x-protobuf";

    public static IServiceCollection AddOtlpReceiver(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddGrpc();
        services.AddOptions<ReceiverOptions>()
            .Bind(configuration.GetSection(ReceiverOptions.SectionName))
            .ValidateDataAnnotations();
        services.AddSingleton<ReceiverDiagnostics>();
        services.AddSingleton<IIngestDiagnostics>(sp => sp.GetRequiredService<ReceiverDiagnostics>());

        return services;
    }

    // Wires both transports. Called by the host and by integration tests so the test
    // exercises the real endpoint registration, not a divergent copy.
    public static void MapOtlpReceiver(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // OTLP/gRPC on :4317
        endpoints.MapGrpcService<TraceExportService>();
        endpoints.MapGrpcService<LogsExportService>();
        endpoints.MapGrpcService<MetricsExportService>();

        // OTLP/HTTP on :4318 (same protobuf bodies)
        endpoints.MapPost("/v1/traces", HandleTracesAsync);
        endpoints.MapPost("/v1/logs", HandleLogsAsync);
        endpoints.MapPost("/v1/metrics", HandleMetricsAsync);
    }

    private static async Task HandleTracesAsync(
        HttpContext context, ITraceSink sink, ReceiverDiagnostics diagnostics)
    {
        if (!IsProtobuf(context.Request.ContentType))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        ExportTraceServiceRequest request;
        try
        {
            request = ExportTraceServiceRequest.Parser.ParseFrom(await ReadBodyAsync(context));
        }
        catch (InvalidProtocolBufferException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var (spans, rejected) = OtlpMapper.MapTraces(request.ResourceSpans);
        foreach (var span in spans)
        {
            await sink.AcceptAsync(span, context.RequestAborted);
        }

        var response = new ExportTraceServiceResponse();
        if (rejected > 0)
        {
            diagnostics.AddDroppedSpans(rejected);
            response.PartialSuccess = new ExportTracePartialSuccess { RejectedSpans = rejected };
        }
        await WriteProtobufAsync(context, response);
    }

    private static async Task HandleLogsAsync(
        HttpContext context, ILogSink sink, ReceiverDiagnostics diagnostics)
    {
        if (!IsProtobuf(context.Request.ContentType))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        ExportLogsServiceRequest request;
        try
        {
            request = ExportLogsServiceRequest.Parser.ParseFrom(await ReadBodyAsync(context));
        }
        catch (InvalidProtocolBufferException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var (logs, rejected) = OtlpMapper.MapLogs(request.ResourceLogs);
        foreach (var log in logs)
        {
            await sink.AcceptAsync(log, context.RequestAborted);
        }

        var response = new ExportLogsServiceResponse();
        if (rejected > 0)
        {
            diagnostics.AddDroppedLogs(rejected);
            response.PartialSuccess = new ExportLogsPartialSuccess { RejectedLogRecords = rejected };
        }
        await WriteProtobufAsync(context, response);
    }

    private static async Task HandleMetricsAsync(
        HttpContext context, IMetricSink sink, ReceiverDiagnostics diagnostics)
    {
        if (!IsProtobuf(context.Request.ContentType))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        ExportMetricsServiceRequest request;
        try
        {
            request = ExportMetricsServiceRequest.Parser.ParseFrom(await ReadBodyAsync(context));
        }
        catch (InvalidProtocolBufferException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var (points, rejected) = OtlpMapper.MapMetrics(request.ResourceMetrics);
        foreach (var point in points)
        {
            await sink.AcceptAsync(point, context.RequestAborted);
        }

        var response = new ExportMetricsServiceResponse();
        if (rejected > 0)
        {
            diagnostics.AddDroppedMetrics(rejected);
            response.PartialSuccess = new ExportMetricsPartialSuccess { RejectedDataPoints = rejected };
        }
        await WriteProtobufAsync(context, response);
    }

    private static bool IsProtobuf(string? contentType) =>
        contentType is not null
        && contentType.StartsWith(ProtobufContentType, StringComparison.OrdinalIgnoreCase);

    private static async Task<byte[]> ReadBodyAsync(HttpContext context)
    {
        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);
        return buffer.ToArray();
    }

    private static async Task WriteProtobufAsync(HttpContext context, IMessage message)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = ProtobufContentType;
        await context.Response.Body.WriteAsync(message.ToByteArray(), context.RequestAborted);
    }
}
