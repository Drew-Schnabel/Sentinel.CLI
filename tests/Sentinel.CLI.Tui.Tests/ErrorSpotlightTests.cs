using FluentAssertions;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Spans;
using Sentinel.CLI.Tui.Views;

namespace Sentinel.CLI.Tui.Tests;

public class ErrorSpotlightTests
{
    private static Span MakeSpan(
        SpanStatus status, TelemetryAttributes? attributes = null, IReadOnlyList<SpanEvent>? events = null)
        => Span.Create(
            TraceId.Parse("4bf92f3577b34da6a3ce929d0e0e4736"),
            SpanId.Parse("00f067aa0ba902b7"),
            parentSpanId: null,
            ServiceName.From("svc"),
            "op",
            SpanKind.Internal,
            status,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddMilliseconds(10),
            attributes,
            events);

    private static TelemetryAttributes Attrs(params (string Key, string Value)[] pairs)
        => TelemetryAttributes.From(
            pairs.ToDictionary(p => p.Key, p => (AttributeValue)new AttributeValue.Text(p.Value), StringComparer.Ordinal));

    [Fact]
    public void For_ok_span_with_no_exception_returns_nothing()
        => ErrorSpotlight.For(MakeSpan(SpanStatus.Ok)).Should().BeEmpty();

    [Fact]
    public void For_error_span_leads_with_the_status_message()
    {
        var lines = ErrorSpotlight.For(MakeSpan(SpanStatus.Error("upstream connection refused")));

        lines.Should().NotBeEmpty();
        lines[0].Should().Contain("ERROR");
        string.Join('\n', lines).Should().Contain("upstream connection refused");
    }

    [Fact]
    public void For_surfaces_exception_attributes_even_when_status_is_unset()
    {
        var span = MakeSpan(
            SpanStatus.Unset,
            Attrs(("exception.type", "TimeoutException"), ("exception.message", "timed out after 30s")));

        var text = string.Join('\n', ErrorSpotlight.For(span));

        text.Should().Contain("TimeoutException").And.Contain("timed out after 30s");
    }

    [Fact]
    public void For_reads_exception_from_an_otel_exception_event()
    {
        var ev = SpanEvent.Create(
            DateTimeOffset.UnixEpoch.AddMilliseconds(5),
            "exception",
            Attrs(("exception.type", "IOException"), ("exception.message", "disk full")));
        var span = MakeSpan(SpanStatus.Error(), events: [ev]);

        var text = string.Join('\n', ErrorSpotlight.For(span));

        text.Should().Contain("IOException").And.Contain("disk full");
    }

    [Fact]
    public void For_truncates_a_long_stacktrace()
    {
        var stack = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"at Frame{i}()"));
        var span = MakeSpan(SpanStatus.Error(), Attrs(("exception.stacktrace", stack)));

        var text = string.Join('\n', ErrorSpotlight.For(span));

        text.Should().Contain("at Frame1()");
        text.Should().NotContain("at Frame20()");   // truncated
        text.Should().Contain("more lines");          // …with a truncation note
    }
}
