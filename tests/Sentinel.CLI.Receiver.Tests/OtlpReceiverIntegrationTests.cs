using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Google.Protobuf;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Sentinel.CLI.Receiver.Tests.TestSupport;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Sentinel.CLI.Receiver.Tests;

public class OtlpReceiverIntegrationTests
{
    private const string ProtobufContentType = "application/x-protobuf";

    static OtlpReceiverIntegrationTests()
    {
        // Allow HTTP/2 over cleartext (h2c) — gRPC on loopback has no TLS.
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    private static GrpcChannel ChannelTo(string address) =>
        GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpClient = new HttpClient
            {
                DefaultRequestVersion = HttpVersion.Version20,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            },
        });

    [Fact]
    public async Task Grpc_valid_export_reaches_store_and_returns_response()
    {
        await using var host = await ReceiverHost.StartAsync(HttpProtocols.Http2);
        using var channel = ChannelTo(host.BaseAddress);
        var client = new TraceService.TraceServiceClient(channel);

        var response = await client.ExportAsync(Proto.TraceRequest(Proto.Span()));

        response.Should().NotBeNull();
        host.Sink.Spans.Should().ContainSingle();
    }

    [Fact]
    public async Task Grpc_export_with_one_bad_span_reports_partial_success_and_keeps_good_span()
    {
        await using var host = await ReceiverHost.StartAsync(HttpProtocols.Http2);
        using var channel = ChannelTo(host.BaseAddress);
        var client = new TraceService.TraceServiceClient(channel);

        var request = Proto.TraceRequest(
            Proto.Span(spanId: Proto.SpanIdBytes(0x01)),
            Proto.Span(spanId: Proto.SpanIdBytes(0x02), name: ""));

        var response = await client.ExportAsync(request);

        response.PartialSuccess.RejectedSpans.Should().Be(1);
        host.Sink.Spans.Should().ContainSingle();
    }

    [Fact]
    public async Task Http_valid_export_returns_200_and_reaches_store()
    {
        await using var host = await ReceiverHost.StartAsync(HttpProtocols.Http1);
        using var http = new HttpClient { BaseAddress = new Uri(host.BaseAddress) };

        using var content = new ByteArrayContent(Proto.TraceRequest(Proto.Span()).ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue(ProtobufContentType);
        var response = await http.PostAsync("/v1/traces", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Sink.Spans.Should().ContainSingle();
    }

    [Fact]
    public async Task Http_malformed_protobuf_returns_400_and_does_not_crash()
    {
        await using var host = await ReceiverHost.StartAsync(HttpProtocols.Http1);
        using var http = new HttpClient { BaseAddress = new Uri(host.BaseAddress) };

        // Field 1 declared as a 5-byte length-delimited submessage, but truncated.
        using var content = new ByteArrayContent([0x0A, 0x05, 0x01]);
        content.Headers.ContentType = new MediaTypeHeaderValue(ProtobufContentType);
        var response = await http.PostAsync("/v1/traces", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Sink.Spans.Should().BeEmpty();
    }

    [Fact]
    public async Task Http_missing_content_type_returns_415()
    {
        await using var host = await ReceiverHost.StartAsync(HttpProtocols.Http1);
        using var http = new HttpClient { BaseAddress = new Uri(host.BaseAddress) };

        using var content = new ByteArrayContent(Proto.TraceRequest(Proto.Span()).ToByteArray());
        var response = await http.PostAsync("/v1/traces", content);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Http_json_content_type_returns_415()
    {
        await using var host = await ReceiverHost.StartAsync(HttpProtocols.Http1);
        using var http = new HttpClient { BaseAddress = new Uri(host.BaseAddress) };

        using var content = new ByteArrayContent(Proto.TraceRequest(Proto.Span()).ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var response = await http.PostAsync("/v1/traces", content);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }
}
