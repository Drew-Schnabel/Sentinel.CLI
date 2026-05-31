using FluentAssertions;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Tui.Views;

namespace Sentinel.CLI.Tui.Tests;

// Guards the live-refresh "keep the user on the span they were inspecting" behavior:
// MainWindow.ResolveSpanIndex maps a previously-selected span id to its new row index.
public class ResolveSpanIndexTests
{
    private static readonly SpanId A = SpanId.Parse("aaaaaaaaaaaaaaaa");
    private static readonly SpanId B = SpanId.Parse("bbbbbbbbbbbbbbbb");
    private static readonly SpanId C = SpanId.Parse("cccccccccccccccc");

    [Fact]
    public void Keeps_the_previously_selected_span()
        => MainWindow.ResolveSpanIndex([A, B, C], B).Should().Be(1);

    [Fact]
    public void Falls_back_to_first_when_previous_is_null()
        => MainWindow.ResolveSpanIndex([A, B, C], null).Should().Be(0);

    [Fact]
    public void Falls_back_to_first_when_previous_span_is_gone()
        => MainWindow.ResolveSpanIndex([A, C], B).Should().Be(0);

    [Fact]
    public void Falls_back_to_first_on_empty_list()
        => MainWindow.ResolveSpanIndex([], A).Should().Be(0);
}
