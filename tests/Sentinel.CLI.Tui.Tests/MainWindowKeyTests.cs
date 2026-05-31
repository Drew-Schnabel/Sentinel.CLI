using System.Runtime.CompilerServices;
using FluentAssertions;
using Sentinel.CLI.Application.Telemetry.Diagnostics;
using Sentinel.CLI.Application.Telemetry.Ports;
using Sentinel.CLI.Application.Telemetry.Queries;
using Sentinel.CLI.Domain.Telemetry.Metrics;
using Sentinel.CLI.Tui.Fixtures;
using Sentinel.CLI.Tui.Views;
using Terminal.Gui.Input;

namespace Sentinel.CLI.Tui.Tests;

// Drives the window's key handling headlessly (no real terminal) by injecting keys via
// NewKeyDownEvent and asserting the view actually changes. This is the regression guard for
// "the keybindings don't work" — they route through MainWindow.OnKeyDownNotHandled.
public class MainWindowKeyTests
{
    private static MainWindow NewWindow(IStoreControl? storeControl = null)
    {
        var fixtures = new FixtureTraceQueries();
        return new MainWindow(
            fixtures,
            fixtures,
            new FakeMetricQueries(),
            new FakeStats(),
            new FakeDiagnostics(),
            storeControl ?? new FakeStoreControl());
    }

    [Theory]
    [InlineData(nameof(Key.D1), nameof(Command.New))]
    [InlineData(nameof(Key.D2), nameof(Command.Open))]
    [InlineData(nameof(Key.D3), nameof(Command.Save))]
    [InlineData(nameof(Key.D4), nameof(Command.SaveAs))]
    [InlineData(nameof(Key.D5), nameof(Command.Edit))]
    [InlineData(nameof(Key.D0), nameof(Command.Cancel))]
    [InlineData(nameof(Key.M), nameof(Command.Toggle))]
    [InlineData(nameof(Key.E), nameof(Command.FindNext))]
    [InlineData(nameof(Key.R), nameof(Command.Refresh))]
    [InlineData(nameof(Key.Q), nameof(Command.Quit))]
    public void MapKey_maps_each_shortcut_to_its_command(string keyName, string expectedCommand)
    {
        var key = keyName switch
        {
            nameof(Key.D0) => Key.D0,
            nameof(Key.D1) => Key.D1,
            nameof(Key.D2) => Key.D2,
            nameof(Key.D3) => Key.D3,
            nameof(Key.D4) => Key.D4,
            nameof(Key.D5) => Key.D5,
            nameof(Key.M) => Key.M,
            nameof(Key.E) => Key.E,
            nameof(Key.R) => Key.R,
            _ => Key.Q,
        };

        MainWindow.MapKey(key).ToString().Should().Be(expectedCommand);
    }

    [Fact]
    public void MapKey_returns_null_for_unmapped_keys()
        => MainWindow.MapKey(Key.A).Should().BeNull();

    [Fact]
    public void View_commands_switch_to_a_single_signal_view()
    {
        using var window = NewWindow();

        window.InvokeCommand(Command.Open); // bound to '2'
        window.CurrentSolo.Should().Be(PaneId.Waterfall);

        window.InvokeCommand(Command.Save); // bound to '3'
        window.CurrentSolo.Should().Be(PaneId.Logs);

        window.InvokeCommand(Command.Edit); // bound to '5'
        window.CurrentSolo.Should().Be(PaneId.GlobalLogs);
    }

    [Fact]
    public void Cancel_command_returns_to_the_combined_view()
    {
        using var window = NewWindow();
        window.InvokeCommand(Command.New); // bound to '1'
        window.CurrentSolo.Should().Be(PaneId.Traces);

        window.InvokeCommand(Command.Cancel); // bound to '0'

        window.CurrentSolo.Should().BeNull();
    }

    [Fact]
    public void Toggle_command_maximizes_then_restores()
    {
        using var window = NewWindow();

        window.InvokeCommand(Command.Toggle); // bound to 'm'
        window.CurrentSolo.Should().NotBeNull(); // a pane is now maximized

        window.InvokeCommand(Command.Toggle);
        window.CurrentSolo.Should().BeNull(); // back to combined
    }

    // The command bar's gate: while open, keys must fall through to the TextField (not consumed);
    // while closed, the usual shortcuts are intercepted. This is the headless guard for the
    // "command input gets eaten / shortcuts stop working" regression. (It proves the gate logic,
    // not the live wiring — a key actually reaching the field needs a real terminal.)
    [Fact]
    public void Command_bar_gate_passes_keys_through_while_open_and_intercepts_when_closed()
    {
        using var window = NewWindow();

        // Closed: a shortcut is intercepted and acts.
        window.HandleKey(Key.D1).Should().BeTrue();
        window.CurrentSolo.Should().Be(PaneId.Traces);

        // F2 opens the bar and is consumed.
        window.HandleKey(Key.F2).Should().BeTrue();

        // Open: a would-be shortcut falls through to the TextField (not consumed, view unchanged).
        window.HandleKey(Key.D2).Should().BeFalse();
        window.CurrentSolo.Should().Be(PaneId.Traces);

        // Esc closes the bar and is consumed.
        window.HandleKey(Key.Esc).Should().BeTrue();

        // Closed again: shortcuts intercepted once more.
        window.HandleKey(Key.D2).Should().BeTrue();
        window.CurrentSolo.Should().Be(PaneId.Waterfall);
    }

    // ':' has no named Key member; for printable ASCII the KeyCode equals the char's code point,
    // and Key.AsRune (what HandleKey checks) derives ':' from it.
    // The submit → result → UI mapping (split into RunCommand so it's testable without a live
    // keyboard). These cover what the gate test can't: the effect of running a command.
    [Fact]
    public void RunCommand_help_pins_its_output_to_the_details_view()
    {
        using var window = NewWindow();

        window.RunCommand("help");

        window.CurrentSolo.Should().Be(PaneId.Details);
        window.DetailsPinned.Should().BeTrue();
    }

    [Fact]
    public void RunCommand_clear_invokes_the_store_control_without_pinning_or_switching_view()
    {
        var store = new FakeStoreControl();
        using var window = NewWindow(store);

        window.RunCommand("clear");

        store.ClearCount.Should().Be(1);
        window.CurrentSolo.Should().BeNull();   // stays in the combined view
        window.DetailsPinned.Should().BeFalse();
    }

    [Fact]
    public void Navigation_after_help_unpins_the_details_view()
    {
        using var window = NewWindow();
        window.RunCommand("help");
        window.DetailsPinned.Should().BeTrue();

        window.InvokeCommand(Command.Cancel); // '0' → combined view

        window.DetailsPinned.Should().BeFalse();
    }

    [Fact]
    public void RunCommand_unknown_verb_echoes_an_error_without_switching_view()
    {
        using var window = NewWindow();

        window.RunCommand("thmee");

        window.CurrentSolo.Should().BeNull();
        window.DetailsPinned.Should().BeFalse();
    }

    [Fact]
    public void RunCommand_filter_sets_then_clears_the_active_filter()
    {
        using var window = NewWindow();

        window.RunCommand("filter service=orders-api");
        window.ActiveFilter.Should().NotBeNull();
        window.ActiveFilter!.Expression.Should().Be("service=orders-api");

        window.RunCommand("filter"); // no args clears
        window.ActiveFilter.Should().BeNull();
    }

    [Fact]
    public void RunCommand_filter_with_invalid_status_leaves_the_filter_unset()
    {
        using var window = NewWindow();

        window.RunCommand("filter status=bogus");

        window.ActiveFilter.Should().BeNull();
    }

    [Fact]
    public void RunCommand_reset_clears_an_active_filter()
    {
        using var window = NewWindow();
        window.RunCommand("filter service=orders-api");
        window.ActiveFilter.Should().NotBeNull();

        window.RunCommand("reset");

        window.ActiveFilter.Should().BeNull();
    }

    [Fact]
    public void ApplyTheme_sets_the_scheme_on_the_trace_list_pane()
    {
        using var window = NewWindow();

        window.ApplyTheme(Themes.Light);

        window.CurrentTheme.Should().Be(ThemeName.Light);
        // Proves the scheme actually reached the custom-source trace-list pane (no TTY needed).
        window.TraceListScheme.Should().NotBeNull();
        window.TraceListScheme!.Normal.Background.Should().Be(Themes.Light.Background);
    }

    [Fact]
    public void RunCommand_theme_switches_the_active_theme()
    {
        using var window = NewWindow();
        window.CurrentTheme.Should().Be(ThemeName.Dark); // default

        window.RunCommand("theme light");

        window.CurrentTheme.Should().Be(ThemeName.Light);
    }

    [Fact]
    public void RunCommand_theme_with_an_unknown_name_does_not_change_the_theme()
    {
        using var window = NewWindow();

        window.RunCommand("theme solarized");

        window.CurrentTheme.Should().Be(ThemeName.Dark); // unchanged
    }

    [Fact]
    public void Command_echo_lingers_across_refresh_ticks_then_clears()
    {
        using var window = NewWindow();

        window.RunCommand("clear"); // echoes "cleared — all telemetry dropped"
        window.StatusText.Should().Contain("cleared"); // shown immediately

        window.UpdateStatus(); // one refresh tick later — still there (the bug was it vanished here)
        window.StatusText.Should().Contain("cleared");

        for (var i = 0; i < 10; i++)
        {
            window.UpdateStatus(); // enough ticks to age it out
        }
        window.StatusText.Should().NotContain("cleared");
    }

    [Fact]
    public void DiagnoseSelectedTrace_with_no_trace_loaded_is_an_error()
    {
        using var window = NewWindow(); // nothing loaded headlessly

        var result = window.DiagnoseSelectedTrace();

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("no trace selected");
    }

    [Fact]
    public void ExportSelectedTrace_with_no_trace_loaded_is_an_error()
    {
        using var window = NewWindow(); // nothing loaded headlessly

        var result = window.ExportSelectedTrace("ignored.json");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("no trace selected");
    }

    [Fact]
    public void F2_opens_the_command_bar()
    {
        using var window = NewWindow();

        window.HandleKey(Key.F2).Should().BeTrue();
        // While open, a printable falls through to the TextField (not consumed).
        window.HandleKey(Key.D1).Should().BeFalse();
    }

    [Fact]
    public void Esc_returns_from_a_solo_view_to_the_combined_main_page()
    {
        using var window = NewWindow();
        window.InvokeCommand(Command.Open); // '2' → solo Waterfall
        window.CurrentSolo.Should().Be(PaneId.Waterfall);

        window.HandleKey(Key.Esc).Should().BeTrue();

        window.CurrentSolo.Should().BeNull(); // back on the combined main page
    }

    [Fact]
    public void Esc_on_the_main_page_is_a_consumed_no_op()
    {
        using var window = NewWindow();
        window.CurrentSolo.Should().BeNull();

        window.HandleKey(Key.Esc).Should().BeTrue(); // consumed (never quits)

        window.CurrentSolo.Should().BeNull();
    }

    private sealed class FakeMetricQueries : IMetricQueries
    {
        public int SeriesCount => 0;

        public async IAsyncEnumerable<MetricPoint> LatestAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FakeStats : ITelemetryStats
    {
        public int TraceCount => 0;
        public int LogCount => 0;
    }

    private sealed class FakeDiagnostics : IIngestDiagnostics
    {
        public long DroppedSpans => 0;
        public long DroppedLogs => 0;
        public long DroppedMetrics => 0;
    }

    private sealed class FakeStoreControl : IStoreControl
    {
        public int ClearCount { get; private set; }

        public void Clear() => ClearCount++;
        public void SetPaused(bool paused) => IsPaused = paused;
        public bool IsPaused { get; private set; }
        public int TraceCapacity { get; private set; } = 500;
        public void SetTraceCapacity(int max) => TraceCapacity = max;
    }
}
