using Grpc.Core;
using Sentinel.CLI.Application.Telemetry.Ports;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Sentinel.CLI.Receiver.Telemetry;

internal sealed class TraceExportService(ITraceSink sink, ReceiverDiagnostics diagnostics)
    : TraceService.TraceServiceBase
{
    public override async Task<ExportTraceServiceResponse> Export(
        ExportTraceServiceRequest request, ServerCallContext context)
    {
        var (spans, rejected) = OtlpMapper.MapTraces(request.ResourceSpans);
        foreach (var span in spans)
        {
            await sink.AcceptAsync(span, context.CancellationToken);
        }

        var response = new ExportTraceServiceResponse();
        if (rejected > 0)
        {
            diagnostics.AddDroppedSpans(rejected);
            response.PartialSuccess = new ExportTracePartialSuccess { RejectedSpans = rejected };
        }
        return response;
    }
}

internal sealed class LogsExportService(ILogSink sink, ReceiverDiagnostics diagnostics)
    : LogsService.LogsServiceBase
{
    public override async Task<ExportLogsServiceResponse> Export(
        ExportLogsServiceRequest request, ServerCallContext context)
    {
        var (logs, rejected) = OtlpMapper.MapLogs(request.ResourceLogs);
        foreach (var log in logs)
        {
            await sink.AcceptAsync(log, context.CancellationToken);
        }

        var response = new ExportLogsServiceResponse();
        if (rejected > 0)
        {
            diagnostics.AddDroppedLogs(rejected);
            response.PartialSuccess = new ExportLogsPartialSuccess { RejectedLogRecords = rejected };
        }
        return response;
    }
}

internal sealed class MetricsExportService(IMetricSink sink, ReceiverDiagnostics diagnostics)
    : MetricsService.MetricsServiceBase
{
    public override async Task<ExportMetricsServiceResponse> Export(
        ExportMetricsServiceRequest request, ServerCallContext context)
    {
        var (points, rejected) = OtlpMapper.MapMetrics(request.ResourceMetrics);
        foreach (var point in points)
        {
            await sink.AcceptAsync(point, context.CancellationToken);
        }

        var response = new ExportMetricsServiceResponse();
        if (rejected > 0)
        {
            diagnostics.AddDroppedMetrics(rejected);
            response.PartialSuccess = new ExportMetricsPartialSuccess { RejectedDataPoints = rejected };
        }
        return response;
    }
}
