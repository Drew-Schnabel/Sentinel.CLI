using FluentAssertions;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Spans;
using static Sentinel.CLI.Domain.Tests.TestHelpers.SpanBuilder;

namespace Sentinel.CLI.Domain.Tests.Telemetry.Spans;

public class SpanEventsLinksTests
{
    [Fact]
    public void Span_defaults_events_and_links_to_empty()
    {
        var span = Make(Sid(1));

        span.Events.Should().BeEmpty();
        span.Links.Should().BeEmpty();
    }

    [Fact]
    public void SpanEvent_create_defaults_attributes_and_rejects_blank_name()
    {
        var spanEvent = SpanEvent.Create(Epoch, "exception");

        spanEvent.Name.Should().Be("exception");
        spanEvent.Attributes.Count.Should().Be(0);

        var act = () => SpanEvent.Create(Epoch, "   ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SpanLink_create_defaults_attributes()
    {
        var link = SpanLink.Create(TraceId.Parse(DefaultTraceId), SpanId.Parse(Sid(2)));

        link.Attributes.Count.Should().Be(0);
    }

    [Fact]
    public void Span_create_preserves_supplied_events_and_links()
    {
        var spanEvent = SpanEvent.Create(Epoch, "retry");
        var link = SpanLink.Create(TraceId.Parse(DefaultTraceId), SpanId.Parse(Sid(9)));

        var span = Span.Create(
            TraceId.Parse(DefaultTraceId), SpanId.Parse(Sid(1)), parentSpanId: null,
            ServiceName.From("svc"), "op", SpanKind.Internal, SpanStatus.Ok,
            Epoch, Epoch.AddMilliseconds(5),
            events: [spanEvent], links: [link]);

        span.Events.Should().ContainSingle().Which.Name.Should().Be("retry");
        span.Links.Should().ContainSingle().Which.SpanId.Value.Should().Be(Sid(9));
    }
}
