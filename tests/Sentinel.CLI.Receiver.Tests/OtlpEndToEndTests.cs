using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Google.Protobuf;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.CLI.Application.Telemetry.Queries;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Receiver.Tests.TestSupport;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Sentinel.CLI.Receiver.Tests;

// The one test that runs the whole product path with no fakes: OTLP bytes over real gRPC →
// mapper → real InMemoryTelemetryStore → ITraceQueries → Trace.Assemble(). Proves the headline
// feature (cross-service assembly) end to end. TUI rendering is the only thing not exercised.
public class OtlpEndToEndTests
{
    static OtlpEndToEndTests()
        => AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

    [Fact]
    public async Task Grpc_export_flows_into_the_real_store_and_assembles_cross_service_trace()
    {
        await using var host = await ReceiverHost.StartWithRealStoreAsync(HttpProtocols.Http2);
        using var channel = GrpcChannel.ForAddress(host.BaseAddress, new GrpcChannelOptions
        {
            HttpClient = new HttpClient
            {
                DefaultRequestVersion = HttpVersion.Version20,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            },
        });
        var client = new TraceService.TraceServiceClient(channel);

        // One trace, three spans, spanning two services: root(svc-a) → child(svc-b) → grandchild(svc-a).
        var traceId = Proto.TraceIdBytes(0x42);
        var rootId = Proto.SpanIdBytes(0x01);
        var childId = Proto.SpanIdBytes(0x02);
        var grandchildId = Proto.SpanIdBytes(0x03);

        var request = Proto.MultiServiceTraceRequest(
            ("svc-a",
            [
                Proto.Span(traceId, rootId, parentSpanId: null, startNanos: 1_000_000_000, endNanos: 1_100_000_000),
                Proto.Span(traceId, grandchildId, parentSpanId: childId, startNanos: 1_020_000_000, endNanos: 1_080_000_000),
            ]),
            ("svc-b",
            [
                Proto.Span(traceId, childId, parentSpanId: rootId, startNanos: 1_010_000_000, endNanos: 1_090_000_000),
            ]));

        await client.ExportAsync(request);

        // Read back from the SAME provider's store and assemble.
        var queries = host.Services.GetRequiredService<ITraceQueries>();
        var domainTraceId = TraceId.Parse(Convert.ToHexStringLower(traceId));
        var trace = await queries.FindAsync(domainTraceId, CancellationToken.None);

        trace.Should().NotBeNull();
        trace!.Spans.Should().HaveCount(3);

        var roots = trace.Assemble().Roots;
        roots.Should().ContainSingle("the three spans share one trace and assemble into one tree");

        var root = roots[0];
        root.Span.SpanId.Value.Should().Be(Convert.ToHexStringLower(rootId));
        root.Span.Service.Value.Should().Be("svc-a");

        var child = root.Children.Should().ContainSingle().Subject;
        child.Span.SpanId.Value.Should().Be(Convert.ToHexStringLower(childId));
        child.Span.Service.Value.Should().Be("svc-b"); // assembly keys on trace/span id, not service

        var grandchild = child.Children.Should().ContainSingle().Subject;
        grandchild.Span.SpanId.Value.Should().Be(Convert.ToHexStringLower(grandchildId));
        grandchild.Span.Service.Value.Should().Be("svc-a");
    }

    [Fact]
    public async Task Http_metrics_export_reaches_the_real_metric_store()
    {
        await using var host = await ReceiverHost.StartWithRealStoreAsync(HttpProtocols.Http1);
        using var http = new HttpClient { BaseAddress = new Uri(host.BaseAddress) };

        using var content = new ByteArrayContent(Proto.MetricRequest(Proto.Gauge("cpu.usage", 0.42)).ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        var response = await http.PostAsync("/v1/metrics", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Services.GetRequiredService<IMetricQueries>().SeriesCount.Should().Be(1);
    }
}
