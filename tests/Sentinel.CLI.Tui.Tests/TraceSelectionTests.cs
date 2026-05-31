using FluentAssertions;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Spans;
using Sentinel.CLI.Tui.Views;

namespace Sentinel.CLI.Tui.Tests;

public class TraceSelectionTests
{
    private const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string C = "cccccccccccccccccccccccccccccccc";

    private static TraceSummary Summary(string traceIdHex) =>
        new(TraceId.Parse(traceIdHex), "root", "svc", 1, TimeSpan.FromMilliseconds(10),
            SpanStatusCode.Ok, DateTimeOffset.UnixEpoch);

    [Fact]
    public void ResolveIndex_empty_list_returns_minus_one()
        => TraceSelection.ResolveIndex([], TraceId.Parse(A)).Should().Be(-1);

    [Fact]
    public void ResolveIndex_null_previous_selects_newest()
        => TraceSelection.ResolveIndex([Summary(A), Summary(B)], previousId: null).Should().Be(0);

    [Fact]
    public void ResolveIndex_previous_still_present_keeps_it()
        => TraceSelection.ResolveIndex([Summary(A), Summary(B), Summary(C)], TraceId.Parse(B)).Should().Be(1);

    [Fact]
    public void ResolveIndex_previous_evicted_falls_back_to_newest()
        => TraceSelection.ResolveIndex([Summary(A), Summary(B)], TraceId.Parse(C)).Should().Be(0);
}
