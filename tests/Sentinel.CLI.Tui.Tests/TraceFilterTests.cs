using FluentAssertions;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Spans;
using Sentinel.CLI.Tui.Fixtures;
using Sentinel.CLI.Tui.Views;

namespace Sentinel.CLI.Tui.Tests;

public class TraceFilterTests
{
    [Fact]
    public void Create_with_no_criteria_returns_no_filter_and_no_error()
    {
        var (filter, error) = TraceFilter.Create(null, null, []);

        filter.Should().BeNull();
        error.Should().BeNull();
    }

    [Fact]
    public void Create_with_invalid_status_returns_an_error()
    {
        var (filter, error) = TraceFilter.Create(null, "bogus", []);

        filter.Should().BeNull();
        error.Should().Contain("unknown status 'bogus'");
    }

    [Theory]
    [InlineData("error")]
    [InlineData("err")]
    [InlineData("ok")]
    [InlineData("unset")]
    [InlineData("none")]
    public void Create_accepts_known_status_aliases(string status)
        => TraceFilter.Create(null, status, []).Error.Should().BeNull();

    [Fact]
    public void Create_normalizes_the_expression()
    {
        var (filter, _) = TraceFilter.Create("orders-api", "ERROR", ["checkout"]);

        filter!.Expression.Should().Be("service=orders-api status=error checkout");
    }

    // ---- Matching against the real UI fixtures (3 traces) ---------------------
    // crossService: orders-api (root) + payment-service + notification-service, status Ok
    // singleService: orders-api, status Ok
    // withError: payment-service (root), status Error

    private static int CountMatching(string? service, string? status, params string[] terms)
    {
        var (filter, error) = TraceFilter.Create(service, status, terms);
        error.Should().BeNull();
        filter.Should().NotBeNull();
        return FixtureTraces.Build()
            .Count(f => filter!.Matches(f.Trace, TraceSummary.FromTrace(f.Trace).Status, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Matches_status_error_selects_only_the_error_trace()
        => CountMatching(service: null, status: "error").Should().Be(1);

    [Fact]
    public void Matches_status_ok_selects_the_two_ok_traces()
        => CountMatching(service: null, status: "ok").Should().Be(2);

    [Fact]
    public void Matches_service_spans_the_whole_trace_not_just_the_root()
    {
        // payment-service is a NON-root span in crossService and the ROOT in withError — so a
        // service filter must hit both, proving matching looks at every span, not only the root.
        CountMatching("payment-service", status: null).Should().Be(2);
    }

    [Fact]
    public void Matches_service_is_case_insensitive_and_substring()
        => CountMatching("NOTIFICATION", status: null).Should().Be(1);

    [Fact]
    public void Matches_free_text_searches_span_names()
        => CountMatching(service: null, status: null, "health").Should().Be(1); // GET /api/health

    [Fact]
    public void Matches_multiple_terms_are_anded()
    {
        // "checkout" (root name) and "email" (notification span) both live in crossService only.
        CountMatching(service: null, status: null, "checkout", "email").Should().Be(1);
        // a term that appears in no trace rules everything out
        CountMatching(service: null, status: null, "checkout", "nonexistent").Should().Be(0);
    }

    [Fact]
    public void Matches_combines_service_and_status_with_and()
    {
        // payment-service is in two traces, but only one of those is an error.
        CountMatching("payment-service", "error").Should().Be(1);
    }

    // ---- since= time window (deterministic clock) -----------------------------

    private static Trace TraceStartingAt(DateTimeOffset start, char id)
    {
        var traceId = TraceId.Parse(new string(id, 32));
        var spanId = SpanId.Parse(new string(id, 16));
        var trace = Trace.Empty(traceId);
        trace.Record(Span.Create(
            traceId, spanId, parentSpanId: null, ServiceName.From("svc"), "op",
            SpanKind.Internal, SpanStatus.Ok, start, start.AddMilliseconds(5)));
        return trace;
    }

    [Fact]
    public void Create_with_invalid_since_returns_an_error()
    {
        var (filter, error) = TraceFilter.Create(null, null, [], sinceText: "5x");

        filter.Should().BeNull();
        error.Should().Contain("invalid duration '5x'");
    }

    [Fact]
    public void Create_includes_since_in_the_expression()
        => TraceFilter.Create("api", null, [], sinceText: "5m").Filter!.Expression
            .Should().Be("service=api since=5m");

    [Fact]
    public void Matches_since_excludes_traces_older_than_the_window()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var (filter, _) = TraceFilter.Create(null, null, [], sinceText: "1m");

        var recent = TraceStartingAt(now.AddSeconds(-30), 'a'); // inside the 1-minute window
        var old = TraceStartingAt(now.AddMinutes(-2), 'b');     // outside it

        filter!.Matches(recent, SpanStatusCode.Ok, now).Should().BeTrue();
        filter.Matches(old, SpanStatusCode.Ok, now).Should().BeFalse();
    }
}
