// TextView is marked obsolete in 2.4 in favor of the external Editor package; for a read-only
// detail pane the existing widget is fine for the spike.
#pragma warning disable CS0618

using System.Collections.ObjectModel;
using Sentinel.CLI.Application.Serialization;
using Sentinel.CLI.Application.Telemetry.Diagnostics;
using Sentinel.CLI.Application.Telemetry.Ports;
using Sentinel.CLI.Application.Telemetry.Queries;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Logs;
using Sentinel.CLI.Domain.Telemetry.Metrics;
using Sentinel.CLI.Domain.Telemetry.Spans;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TGuiApp = Terminal.Gui.App.Application;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Sentinel.CLI.Tui.Views;

internal sealed class MainWindow : Window
{
    private readonly ITraceQueries _traceQueries;
    private readonly ILogQueries _logQueries;
    private readonly IMetricQueries _metricQueries;
    private readonly ITelemetryStats _stats;
    private readonly IIngestDiagnostics _diagnostics;

    private readonly ListView _traceList = new();
    private readonly ListView _waterfall = new();
    private readonly TextView _details = new();
    private readonly ListView _logsList = new();
    private readonly ListView _metricsList = new();
    private readonly ListView _globalLogsList = new();
    private readonly Label _statusLabel = new();
    private readonly Label _commandPrompt = new();
    private readonly TextField _commandBar = new();

    private FrameView _tracesPane = null!;
    private FrameView _waterfallPane = null!;
    private FrameView _detailsPane = null!;
    private FrameView _logsPane = null!;
    private FrameView _metricsPane = null!;
    private FrameView _globalLogsPane = null!;

    // null = combined 4-pane view; otherwise that one pane is shown full-screen.
    private PaneId? _solo;

    private List<TraceSummary> _traces = [];
    private List<string> _rowText = [];
    private List<WaterfallRow> _waterfallRows = [];
    private List<LogRecord> _selectedTraceLogs = [];

    // Rendered text currently shown in the waterfall/logs panes for the open trace. Used to
    // skip rebuilding (and resetting scroll/selection) on a refresh tick when nothing changed.
    private List<string> _waterfallText = [];
    private List<string> _logText = [];

    // Per-row severities for the Logs pane, in display order — parallel to _logText. Read by
    // the RowRender handler to color each row by severity.
    private List<LogSeverity> _logSeverities = [];

    // Global log-stream pane state: displayed text (for skip-if-unchanged) + parallel severities.
    private List<string> _globalLogText = [];
    private List<LogSeverity> _globalLogSeverities = [];

    // Stable per-service colors for the trace list and waterfall (first-seen assignment). Rebuilt
    // from the active theme's palette when `:theme` switches.
    private Theme _theme = Themes.Default;
    private ServiceColorMap _serviceColors = new(Themes.Default.ServicePalette);

    // Rolling per-series value history for the metrics sparklines (≈ this many seconds of trend
    // at the 1s refresh tick). Sampled every tick so the trend is ready when the view is opened.
    private readonly MetricSparklines _metricHistory = new(capacity: 24);

    // Set while the trace list OR the waterfall is rebuilt programmatically so the
    // selection-changed handlers don't reload/reset and yank the user out of their place.
    private bool _suppressSelectionEvents;

    // True while the `:` command bar is open: the global key interceptor lets keystrokes fall
    // through to the command TextField instead of treating them as shortcuts.
    private bool _commandBarOpen;

    // True while the Details pane shows pinned command output (e.g. `:help`). The refresh tick
    // leaves Details untouched while pinned, so the output isn't clobbered ~1s later; any
    // navigation (view switch or trace/span selection) unpins it.
    private bool _detailsPinned;

    // Verb registry for the command bar; commands act through the injected store-control port.
    private readonly CommandRegistry _commands;
    private readonly IStoreControl _storeControl;

    // Active trace-list filter (`:filter`/`:search`), or null for "show all". View-only — the
    // store is untouched. Read at the top of PopulateAsync (captured once, off the UI thread).
    private TraceFilter? _activeFilter;

    // Status-line suffix describing the active filter + match count, recomputed each tick.
    private string? _filterSummary;

    // A transient command echo shown alongside the counts and held for a few refresh ticks so it's
    // readable (a single tick — the old behavior — vanished within ~1s). Tick-based rather than
    // wall-clock to stay deterministic/testable; tuned for the default ~1s refresh.
    private const int CommandMessageTicks = 5;
    private string? _commandMessage;
    private int _commandMessageTicksRemaining;

    public MainWindow(
        ITraceQueries traceQueries,
        ILogQueries logQueries,
        IMetricQueries metricQueries,
        ITelemetryStats stats,
        IIngestDiagnostics diagnostics,
        IStoreControl storeControl)
    {
        _traceQueries = traceQueries;
        _logQueries = logQueries;
        _metricQueries = metricQueries;
        _stats = stats;
        _diagnostics = diagnostics;
        _storeControl = storeControl;
        _commands = new CommandRegistry(storeControl, ApplyFilter, ExportSelectedTrace, ApplyTheme, DiagnoseSelectedTrace);

        _tracesPane = BuildTracesPane();
        _waterfallPane = BuildWaterfallPane();
        _detailsPane = BuildDetailsPane();
        _logsPane = BuildLogsPane();
        _metricsPane = BuildMetricsPane();
        _globalLogsPane = BuildGlobalLogsPane();

        _statusLabel.X = 0;
        _statusLabel.Y = Pos.AnchorEnd(1);
        _statusLabel.Width = Dim.Fill();
        _statusLabel.Height = 1;
        _statusLabel.CanFocus = false;

        // Command bar lives in the header (top row), hidden until F2 opens it. A leading prompt
        // label makes it read as a command line; the TextField fills the rest of the row. Both are
        // shown/hidden together and overlay the top of the panes only while open. The TextField is
        // focusable (to receive typed chars) but kept out of the Tab cycle.
        _commandPrompt.Text = "F2 ▸";
        _commandPrompt.X = 0;
        _commandPrompt.Y = 0;
        _commandPrompt.Height = 1;
        _commandPrompt.Width = 5;
        _commandPrompt.Visible = false;
        _commandPrompt.CanFocus = false;

        _commandBar.X = Pos.Right(_commandPrompt) + 1;
        _commandBar.Y = 0;
        _commandBar.Width = Dim.Fill();
        _commandBar.Height = 1;
        _commandBar.Visible = false;
        _commandBar.CanFocus = true;
        _commandBar.TabStop = TabBehavior.NoStop;

        Title = "Sentinel — 1-5 focus · 0 all · m max · e next-error · r refresh · F2 command · q quit";

        Add(_tracesPane, _waterfallPane, _detailsPane, _logsPane, _metricsPane, _globalLogsPane, _statusLabel, _commandPrompt, _commandBar);
        ApplyLayout(); // sets initial combined geometry + visibility
        RegisterCommands();
    }

    // Current full-screen pane, or null for the combined view (exposed for tests).
    internal PaneId? CurrentSolo => _solo;

    // Whether the Details pane is showing pinned command output (e.g. `:help`); exposed for tests.
    internal bool DetailsPinned => _detailsPinned;

    // The active trace-list filter, or null when showing all; exposed for tests.
    internal TraceFilter? ActiveFilter => _activeFilter;

    // Register the shortcut commands on this window. Separate from BindKeys so it has no
    // dependency on a live Application — tests invoke these directly via InvokeCommand.
    private void RegisterCommands()
    {
        AddCommand(Command.Quit, () => { TGuiApp.RequestStop(this); return true; });
        AddCommand(Command.Refresh, () => { _ = RefreshAsync(CancellationToken.None); return true; });
        AddCommand(Command.FindNext, () => { JumpToNextError(); return true; });
        AddCommand(Command.Cancel, () => { ShowCombined(); return true; });
        AddCommand(Command.New, () => { ShowSolo(PaneId.Traces); return true; });
        AddCommand(Command.Open, () => { ShowSolo(PaneId.Waterfall); return true; });
        AddCommand(Command.Save, () => { ShowSolo(PaneId.Logs); return true; });
        AddCommand(Command.SaveAs, () => { ShowSolo(PaneId.Metrics); return true; });
        // The Command values for the solo views are arbitrary slots (see MapKey) — Edit is the
        // 5th, mapped to the global log stream.
        AddCommand(Command.Edit, () => { ShowSolo(PaneId.GlobalLogs); return true; });
        AddCommand(Command.Toggle, () => { ToggleMaximize(); return true; });
    }

    // Handle shortcuts on the raw Application.KeyDown event — it fires for every key before the
    // focused view, so it beats the ListView's type-ahead, which otherwise swallows letters and
    // digits (the focused list never lets them bubble to a binding). Marking the key Handled
    // stops further processing. Called by TuiRunner after Application.Init.
    public void BindKeys()
    {
        // Terminal.Gui binds Esc to Quit. That binding fires even when our KeyDown preview marks the
        // key Handled, so Esc would quit the whole app instead of closing the command bar. Remove it
        // at both the application level and on this window (the running Toplevel) — quitting is
        // q / Ctrl+C (see MapKey), and Esc/F2 cancel the command bar (see HandleKey). Remove() is a
        // no-op if the binding isn't present.
        TGuiApp.Keyboard.KeyBindings.Remove(Key.Esc);
        KeyBindings.Remove(Key.Esc);

        TGuiApp.Keyboard.KeyDown += (_, key) =>
        {
            if (HandleKey(key))
            {
                key.Handled = true;
            }
        };
    }

    // Single decision point for the global key interceptor, exercised directly by tests.
    // Returns true when the key is consumed (the caller marks it Handled, stopping routing).
    //
    // While the command bar is open we consume only Enter (submit) and Esc/F2 (cancel); every other
    // key returns false — deliberately NOT marked Handled — so normal routing delivers it to the
    // focused command TextField. With the bar closed, F2 opens it and the usual shortcuts apply.
    // F2 is a named key (no shifted-character ambiguity), so detection is reliable across terminals.
    internal bool HandleKey(Key key)
    {
        if (_commandBarOpen)
        {
            if (key == Key.Enter)
            {
                SubmitCommandBar();
                return true;
            }
            if (key == Key.Esc || key == Key.F2)
            {
                CloseCommandBar();
                return true;
            }
            return false; // fall through to the TextField
        }

        if (key == Key.F2)
        {
            OpenCommandBar();
            return true;
        }

        // Esc acts as "back": from a single-signal/maximized view it returns to the combined main
        // page; on the main page it's a no-op. Always consumed so it can never quit the app
        // (quitting is q / Ctrl+C — the framework's Esc→Quit binding is removed in BindKeys).
        if (key == Key.Esc)
        {
            if (_solo is not null)
            {
                ShowCombined();
            }
            return true;
        }

        if (MapKey(key) is { } command)
        {
            InvokeCommand(command);
            return true;
        }
        return false;
    }

    private void OpenCommandBar()
    {
        _commandBarOpen = true;
        _commandBar.Text = string.Empty;
        _commandPrompt.Visible = true;
        _commandBar.Visible = true;
        ApplyLayout();          // reflow panes down to make room for the header row
        _commandBar.SetFocus(); // ApplyLayout focuses a pane; override to the command field
    }

    private void CloseCommandBar()
    {
        _commandBarOpen = false;
        _commandBar.Visible = false;
        _commandPrompt.Visible = false;
        _commandBar.Text = string.Empty;
        ApplyLayout(); // reflow panes back up; ApplyLayout restores focus to the active pane
    }

    private void SubmitCommandBar()
    {
        var input = _commandBar.Text;
        CloseCommandBar();
        RunCommand(input);
    }

    // Dispatch + apply, split from the TextField read so the result→UI mapping is unit-testable
    // headlessly (no live keyboard needed).
    internal void RunCommand(string? input) => ApplyCommandResult(_commands.Dispatch(input));

    private void ApplyCommandResult(CommandResult result)
    {
        if (string.IsNullOrEmpty(result.Message))
        {
            return; // blank input — nothing to echo
        }
        if (result.Output == CommandOutput.Details)
        {
            ShowSolo(PaneId.Details); // guarantee the multi-line output is visible (clears the pin)
            _details.Text = result.Message;
            _detailsPinned = true;    // set after ShowSolo so the refresh tick won't clobber it
            return;
        }
        ShowCommandResult(result.Message);
    }

    // Echo a short command result/error on the status line, held for a few refresh ticks (it rides
    // alongside the telemetry counts) so it doesn't vanish before it can be read.
    private void ShowCommandResult(string message)
    {
        _commandMessage = message;
        _commandMessageTicksRemaining = CommandMessageTicks;
        UpdateStatus(); // show immediately, not on the next tick
    }

    // Set (or clear, when null) the trace-list filter and reload from the first matching trace.
    // A fresh load (not a preserving refresh) is intentional: the result set changed, so resetting
    // to the top and repopulating the panes is the predictable behavior.
    private void ApplyFilter(TraceFilter? filter)
    {
        _activeFilter = filter;
        if (filter is null)
        {
            _filterSummary = null;
        }
        _ = LoadAsync(CancellationToken.None);
    }

    // Write the currently selected trace (its assembled spans + correlated logs) to a JSON file.
    // Uses the already-loaded display state (_waterfallRows / _selectedTraceLogs), so it's
    // synchronous — no re-query. Returns the result to echo on the status line. Internal for tests.
    internal CommandResult ExportSelectedTrace(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return CommandResult.Error("usage: export <path>");
        }
        if (CurrentSelectedTraceId() is not { } id || _waterfallRows.Count == 0)
        {
            return CommandResult.Error("no trace selected to export");
        }

        var spans = _waterfallRows.Select(r => r.Span).ToList();
        try
        {
            var json = TraceExporter.ToJson(id, spans, _selectedTraceLogs);
            File.WriteAllText(path, json);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException)
        {
            return CommandResult.Error($"export failed: {ex.Message}");
        }

        return CommandResult.Ok($"exported {path} ({spans.Count} spans, {_selectedTraceLogs.Count} logs)");
    }

    // Run the instrumentation health checks on the selected trace's spans (already loaded — no
    // re-query) and render the findings to the Details pane. Internal for tests.
    internal CommandResult DiagnoseSelectedTrace()
    {
        if (CurrentSelectedTraceId() is not { } id || _waterfallRows.Count == 0)
        {
            return CommandResult.Error("no trace selected to diagnose");
        }

        var spans = _waterfallRows.Select(r => r.Span).ToList();
        var findings = TraceDoctor.Diagnose(spans);

        var lines = new List<string> { $"trace health — {id.Value[..16]}… ({spans.Count} spans)", string.Empty };
        if (findings.Count == 0)
        {
            lines.Add("  OK — no instrumentation issues detected");
        }
        else
        {
            lines.AddRange(findings.Select(f => $"  ! {f}"));
        }

        return CommandResult.Ok(string.Join(Environment.NewLine, lines), CommandOutput.Details);
    }

    // Switch the color theme: apply the base Scheme to the window + every pane (set per-pane rather
    // than relying on cascade), rebuild the service-color map from the new palette, and re-source
    // the colored lists so they repaint. Internal for tests.
    internal void ApplyTheme(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;

        var scheme = theme.BuildScheme();
        SetScheme(scheme);
        foreach (var (_, frame, inner) in Panes())
        {
            frame.SetScheme(scheme);
            inner.SetScheme(scheme);
        }
        _statusLabel.SetScheme(scheme);
        _commandPrompt.SetScheme(scheme);
        _commandBar.SetScheme(scheme);

        // New palette → fresh first-seen assignments. Force the trace-list rebuild (its row colors
        // are baked into the source descriptors; the skip-if-unchanged path would otherwise keep
        // the old palette) while preserving the selected trace. The waterfall/logs recolor live via
        // their RowRender handlers on the redraw, so they don't need re-sourcing.
        _serviceColors = new ServiceColorMap(theme.ServicePalette);
        _rowText = [];
        SetNeedsDraw();
        _ = RefreshAsync(CancellationToken.None);
    }

    // The active theme, exposed for tests.
    internal ThemeName CurrentTheme => _theme.Name;

    // The scheme currently applied to the trace list (the custom-source pane), exposed so tests can
    // prove the theme actually reached it without a real terminal.
    internal Scheme? TraceListScheme => _traceList.GetScheme();

    internal static Command? MapKey(Key key)
    {
        if (key == Key.Q || key == Key.C.WithCtrl)
        {
            return Command.Quit;
        }
        if (key == Key.R)
        {
            return Command.Refresh;
        }
        if (key == Key.E)
        {
            return Command.FindNext;
        }
        if (key == Key.D0)
        {
            return Command.Cancel;
        }
        if (key == Key.D1)
        {
            return Command.New;
        }
        if (key == Key.D2)
        {
            return Command.Open;
        }
        if (key == Key.D3)
        {
            return Command.Save;
        }
        if (key == Key.D4)
        {
            return Command.SaveAs;
        }
        if (key == Key.D5)
        {
            return Command.Edit;
        }
        if (key == Key.M)
        {
            return Command.Toggle;
        }
        return null;
    }


    // ---- view modes -----------------------------------------------------------------------

    public void ShowCombined()
    {
        _detailsPinned = false;
        _solo = null;
        ApplyLayout();
    }

    public void ShowSolo(PaneId pane)
    {
        _detailsPinned = false;
        _solo = pane;
        ApplyLayout();
    }

    // `m`: maximize the focused pane, or restore the combined view if already maximized.
    public void ToggleMaximize()
    {
        _detailsPinned = false;
        _solo = _solo is null ? FocusedPane() : null;
        ApplyLayout();
    }

    private PaneId FocusedPane()
    {
        if (_waterfall.HasFocus) return PaneId.Waterfall;
        if (_details.HasFocus) return PaneId.Details;
        if (_logsList.HasFocus) return PaneId.Logs;
        if (_metricsList.HasFocus) return PaneId.Metrics;
        if (_globalLogsList.HasFocus) return PaneId.GlobalLogs;
        return PaneId.Traces;
    }

    private void ApplyLayout()
    {
        // When the command bar is open it occupies row 0 as a header, so push the panes down one
        // row (they fill from `top` to the status row at the bottom). Closed → panes start at row 0.
        var top = _commandBarOpen ? 1 : 0;

        if (_solo is null)
        {
            SetGeometry(_tracesPane, 0, top, Dim.Percent(35), Dim.Fill(1));
            SetGeometry(_waterfallPane, Pos.Right(_tracesPane), top, Dim.Percent(40), Dim.Fill(1));
            SetGeometry(_detailsPane, Pos.Right(_waterfallPane), top, Dim.Fill(), Dim.Percent(60));
            SetGeometry(_logsPane, Pos.Right(_waterfallPane), Pos.Bottom(_detailsPane), Dim.Fill(), Dim.Fill(1));
        }
        else
        {
            SetGeometry(PaneFrame(_solo.Value), 0, top, Dim.Fill(), Dim.Fill(1));
        }

        foreach (var (id, frame, inner) in Panes())
        {
            var visible = ViewLayout.IsVisible(id, _solo);
            frame.Visible = visible;
            inner.TabStop = visible ? TabBehavior.TabStop : TabBehavior.NoStop;
        }

        (_solo is { } s ? InnerView(s) : _traceList).SetFocus();

        if (_metricsPane.Visible)
        {
            _ = RefreshMetricsAsync(render: true);
        }
        if (_globalLogsPane.Visible)
        {
            _ = RefreshGlobalLogsAsync();
        }
    }

    private static void SetGeometry(View view, Pos x, Pos y, Dim width, Dim height)
    {
        view.X = x;
        view.Y = y;
        view.Width = width;
        view.Height = height;
    }

    private IEnumerable<(PaneId Id, FrameView Frame, View Inner)> Panes()
    {
        yield return (PaneId.Traces, _tracesPane, _traceList);
        yield return (PaneId.Waterfall, _waterfallPane, _waterfall);
        yield return (PaneId.Details, _detailsPane, _details);
        yield return (PaneId.Logs, _logsPane, _logsList);
        yield return (PaneId.Metrics, _metricsPane, _metricsList);
        yield return (PaneId.GlobalLogs, _globalLogsPane, _globalLogsList);
    }

    private FrameView PaneFrame(PaneId id) => id switch
    {
        PaneId.Traces => _tracesPane,
        PaneId.Waterfall => _waterfallPane,
        PaneId.Details => _detailsPane,
        PaneId.Logs => _logsPane,
        PaneId.GlobalLogs => _globalLogsPane,
        _ => _metricsPane,
    };

    private View InnerView(PaneId id) => id switch
    {
        PaneId.Traces => _traceList,
        PaneId.Waterfall => _waterfall,
        PaneId.Details => _details,
        PaneId.Logs => _logsList,
        PaneId.GlobalLogs => _globalLogsList,
        _ => _metricsList,
    };

    // Sample the latest value of every metric series into the rolling history, and (when the
    // metrics view is showing) rebuild the table with a per-series sparkline. Sampling runs every
    // tick even while hidden so the trend is already populated when the view is opened.
    private async Task RefreshMetricsAsync(bool render)
    {
        var points = new List<MetricPoint>();
        await foreach (var metric in _metricQueries.LatestAsync(CancellationToken.None))
        {
            points.Add(metric);
        }

        TGuiApp.Invoke(() =>
        {
            foreach (var metric in points)
            {
                _metricHistory.Add(metric, MetricPresenter.SparkValue(metric));
            }
            if (!render)
            {
                return;
            }
            var rows = points
                .OrderBy(m => m.Service.Value, StringComparer.Ordinal)
                .ThenBy(m => m.Name, StringComparer.Ordinal)
                .Select(FormatMetricRow)
                .ToList();
            _metricsList.SetSource(new ObservableCollection<string>(rows));
        });
    }

    private string FormatMetricRow(MetricPoint metric)
    {
        var spark = Sparkline.Render(_metricHistory.Values(metric));
        var labels = MetricPresenter.FormatLabels(metric);
        var suffix = string.IsNullOrEmpty(labels) ? string.Empty : $"  {{{labels}}}";
        return $"{metric.Service} · {metric.Name} [{metric.Kind}] {spark} {MetricPresenter.FormatValue(metric)}{suffix}";
    }

    // Pull the full recent log stream across all services, newest first, severity-colored.
    // Skips the rebuild when unchanged so the user's scroll isn't reset on an idle tick.
    private async Task RefreshGlobalLogsAsync()
    {
        const int maxRows = 500;
        var logs = new List<LogRecord>();
        await foreach (var log in _logQueries.StreamAsync(CancellationToken.None))
        {
            logs.Add(log);
        }
        var ordered = logs.OrderByDescending(l => l.Timestamp).Take(maxRows).ToList();
        var rows = ordered.Select(LogPresenter.FormatWithService).ToList();
        var severities = ordered.Select(l => l.Severity).ToList();

        TGuiApp.Invoke(() =>
        {
            if (_globalLogText.SequenceEqual(rows, StringComparer.Ordinal))
            {
                return;
            }
            _globalLogText = rows;
            _globalLogSeverities = severities;
            _globalLogsList.SetSource(new ObservableCollection<string>(rows));
        });
    }

    // The current status-line text, exposed for tests.
    internal string StatusText => _statusLabel.Text;

    // Internal so tests can simulate refresh ticks (which is what ages out the command echo).
    internal void UpdateStatus()
    {
        string? message = null;
        if (_commandMessageTicksRemaining > 0)
        {
            message = _commandMessage;
            _commandMessageTicksRemaining--;
        }
        _statusLabel.Text = StatusLine.Format(
            _stats.TraceCount, _stats.LogCount, _metricQueries.SeriesCount,
            _diagnostics.DroppedSpans, _diagnostics.DroppedLogs, _diagnostics.DroppedMetrics,
            _filterSummary, _storeControl.IsPaused, message);
    }

    // Jump the trace-list selection to the next *newer* error trace (upward, toward the top of
    // the newest-first list) and stop at the newest — one-directional, no wrap, so the jump is
    // always predictable. No-op when there is no newer error above the current selection.
    public void JumpToNextError()
    {
        if (_traces.Count == 0)
        {
            return;
        }
        var errorFlags = _traces.Select(t => t.Status == SpanStatusCode.Error).ToList();
        var newer = ErrorNavigation.NewerErrorIndex(errorFlags, _traceList.SelectedItem ?? 0);
        if (newer >= 0)
        {
            _traceList.SelectedItem = newer; // fires OnTraceSelectionChanged → loads that trace
        }
    }

    // First load: select the newest trace and populate the waterfall.
    public Task LoadAsync(CancellationToken cancellationToken)
        => PopulateAsync(preserveSelection: false, cancellationToken);

    // Periodic/manual refresh: pull in newly received telemetry while keeping the user's
    // place. Leaves the inspected trace's waterfall and details untouched.
    public Task RefreshAsync(CancellationToken cancellationToken)
        => PopulateAsync(preserveSelection: true, cancellationToken);

    private async Task PopulateAsync(bool preserveSelection, CancellationToken cancellationToken)
    {
        // Capture the filter once on the UI thread before the first await — the enumeration below
        // may resume off-thread, and TraceFilter is immutable, so this avoids a torn read. `now`
        // anchors any `since=` window so every trace this tick is judged against one instant.
        var filter = _activeFilter;
        var now = DateTimeOffset.UtcNow;

        var traces = new List<Trace>();
        await foreach (var trace in _traceQueries.RecentAsync(200, cancellationToken))
        {
            traces.Add(trace);
        }

        var loaded = new List<TraceSummary>(traces.Count);
        foreach (var trace in traces)
        {
            var summary = TraceSummary.FromTrace(trace);
            if (filter is null || filter.Matches(trace, summary.Status, now))
            {
                loaded.Add(summary);
            }
        }
        var filterSummary = filter is { } f
            ? $"filter [{f.Expression}] {loaded.Count}/{traces.Count}"
            : null;

        var ordered = loaded.OrderByDescending(t => t.StartedAt).ToList();
        // Format the row text off the UI thread (pure); colors are applied inside the Invoke so
        // the per-service color map is only ever touched from the UI thread.
        var formatted = ordered.Select(FormatTraceRow).ToList();
        var newRows = formatted.Select(f => f.Text).ToList();

        TGuiApp.Invoke(() =>
        {
            _filterSummary = filterSummary;
            UpdateStatus(); // refresh totals/dropped every tick, even when the trace list is unchanged

            // Live-refresh the open trace's panes (waterfall/details/logs) keeping the user's
            // span selection. Runs every tick — the trace's spans/logs can change even when its
            // summary row in the list does not — and no-ops internally when nothing changed.
            if (preserveSelection
                && _traceList.SelectedItem is { } sel && sel >= 0 && sel < _traces.Count)
            {
                _ = SelectTraceAsync(_traces[sel].Id, CancellationToken.None, preserveSpan: true);
            }

            // Keep the live firehose flowing while it's the active view.
            if (_globalLogsPane.Visible)
            {
                _ = RefreshGlobalLogsAsync();
            }

            // Sample metric history every tick (even while hidden) so the sparklines show a trend
            // the moment the view is opened; only rebuild the table when it's showing.
            _ = RefreshMetricsAsync(render: _metricsPane.Visible);

            // Nothing visibly changed since the last load — don't rebuild and reset scroll.
            if (preserveSelection && _rowText.SequenceEqual(newRows, StringComparer.Ordinal))
            {
                return;
            }

            var previousId = preserveSelection ? CurrentSelectedTraceId() : null;
            _traces = ordered;
            _rowText = newRows;

            _suppressSelectionEvents = true;
            try
            {
                var descriptors = new List<TraceRow>(ordered.Count);
                for (var i = 0; i < ordered.Count; i++)
                {
                    var t = ordered[i];
                    descriptors.Add(new TraceRow(
                        formatted[i].Text,
                        _serviceColors.ColorFor(t.RootService),
                        _theme.StatusTokenColor(t.Status),
                        formatted[i].StatusStart,
                        formatted[i].StatusLength));
                }
                var previousSource = _traceList.Source;
                _traceList.Source = new TraceListSource(descriptors);
                (previousSource as IDisposable)?.Dispose(); // we own each source we assign
                var index = TraceSelection.ResolveIndex(_traces, previousId);
                if (index >= 0)
                {
                    _traceList.SelectedItem = index;
                }
            }
            finally
            {
                _suppressSelectionEvents = false;
            }

            if (_traces.Count == 0)
            {
                // No traces (e.g. just after `:clear`) — blank the center/right panes so they
                // don't keep showing the last trace's waterfall/logs/details.
                ClearTracePanes();
            }
            else if (!preserveSelection)
            {
                // Only the first load drives the waterfall/details; a refresh leaves them be.
                // Clamp the selection: a stale SelectedItem (e.g. after a filter narrows the list)
                // could otherwise index past the new, shorter list. Count >= 1 in this branch.
                var index = Math.Clamp(_traceList.SelectedItem ?? 0, 0, _traces.Count - 1);
                _ = SelectTraceAsync(_traces[index].Id, CancellationToken.None);
            }
        });
    }

    // Reset the trace-dependent panes (waterfall, logs, details) and their cached text when there
    // is no trace to show, so stale content from the last trace doesn't linger after a `:clear`.
    // Details is left alone while pinned (e.g. `:help` output).
    private void ClearTracePanes()
    {
        _waterfallRows = [];
        _selectedTraceLogs = [];
        _waterfallText = [];
        _logText = [];
        _logSeverities = [];

        _suppressSelectionEvents = true;
        try
        {
            _waterfall.SetSource(new ObservableCollection<string>([]));
            _logsList.SetSource(new ObservableCollection<string>([]));
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        if (!_detailsPinned)
        {
            _details.Text = string.Empty;
        }
    }

    private TraceId? CurrentSelectedTraceId()
    {
        var index = _traceList.SelectedItem;
        return index is { } i && i >= 0 && i < _traces.Count ? _traces[i].Id : null;
    }

    private FrameView BuildTracesPane()
    {
        var frame = new FrameView { Title = "Traces" };

        _traceList.X = 0;
        _traceList.Y = 0;
        _traceList.Width = Dim.Fill();
        _traceList.Height = Dim.Fill();
        _traceList.CanFocus = true;
        _traceList.TabStop = TabBehavior.TabStop;
        _traceList.ValueChanged += OnTraceSelectionChanged;

        frame.Add(_traceList);
        return frame;
    }

    private FrameView BuildWaterfallPane()
    {
        var frame = new FrameView { Title = "Waterfall" };

        _waterfall.X = 0;
        _waterfall.Y = 0;
        _waterfall.Width = Dim.Fill();
        _waterfall.Height = Dim.Fill();
        _waterfall.CanFocus = true;
        _waterfall.TabStop = TabBehavior.TabStop;
        _waterfall.ValueChanged += OnSpanSelectionChanged;
        _waterfall.RowRender += OnWaterfallRowRender;

        frame.Add(_waterfall);
        return frame;
    }

    private FrameView BuildDetailsPane()
    {
        var frame = new FrameView { Title = "Details" };

        _details.X = 0;
        _details.Y = 0;
        _details.Width = Dim.Fill();
        _details.Height = Dim.Fill();
        _details.ReadOnly = true;
        _details.WordWrap = true;
        _details.CanFocus = true;
        _details.TabStop = TabBehavior.TabStop;

        frame.Add(_details);
        return frame;
    }

    private FrameView BuildLogsPane()
    {
        var frame = new FrameView { Title = "Logs (selected trace)" };

        _logsList.X = 0;
        _logsList.Y = 0;
        _logsList.Width = Dim.Fill();
        _logsList.Height = Dim.Fill();
        _logsList.CanFocus = true;
        _logsList.TabStop = TabBehavior.TabStop;
        _logsList.RowRender += OnLogRowRender;

        frame.Add(_logsList);
        return frame;
    }

    private FrameView BuildGlobalLogsPane()
    {
        var frame = new FrameView { Title = "Logs — all services (live)" };

        _globalLogsList.X = 0;
        _globalLogsList.Y = 0;
        _globalLogsList.Width = Dim.Fill();
        _globalLogsList.Height = Dim.Fill();
        _globalLogsList.CanFocus = true;
        _globalLogsList.TabStop = TabBehavior.TabStop;
        _globalLogsList.RowRender += OnGlobalLogRowRender;

        frame.Add(_globalLogsList);
        return frame;
    }

    private FrameView BuildMetricsPane()
    {
        var frame = new FrameView { Title = "Metrics (latest per series)" };

        _metricsList.X = 0;
        _metricsList.Y = 0;
        _metricsList.Width = Dim.Fill();
        _metricsList.Height = Dim.Fill();
        _metricsList.CanFocus = true;
        _metricsList.TabStop = TabBehavior.TabStop;

        frame.Add(_metricsList);
        return frame;
    }

    private void OnTraceSelectionChanged(object? sender, ValueChangedEventArgs<int?> e)
    {
        if (_suppressSelectionEvents)
        {
            return;
        }
        _detailsPinned = false; // a user-driven trace change releases pinned command output
        var index = e.NewValue;
        if (!index.HasValue || index.Value < 0 || index.Value >= _traces.Count)
        {
            return;
        }
        _ = SelectTraceAsync(_traces[index.Value].Id, CancellationToken.None);
    }

    private void OnSpanSelectionChanged(object? sender, ValueChangedEventArgs<int?> e)
    {
        if (_suppressSelectionEvents)
        {
            return; // programmatic rebuild — details are set explicitly by the caller
        }
        _detailsPinned = false; // a user-driven span change releases pinned command output
        var index = e.NewValue;
        if (!index.HasValue || index.Value < 0 || index.Value >= _waterfallRows.Count)
        {
            _details.Text = string.Empty;
            return;
        }
        var span = _waterfallRows[index.Value].Span;
        _details.Text = RenderDetails(span, _selectedTraceLogs);
    }

    // Color each Logs row by severity (errors red, warnings yellow, debug/trace dim). The
    // selected row is left at the list's focus color so the cursor stays visible.
    private void OnLogRowRender(object? sender, ListViewRowEventArgs e)
    {
        // Skip the selected row only while the pane is focused — an unfocused list has no
        // selection highlight to preserve, so its current row should still show its severity.
        if (e.Row < 0 || e.Row >= _logSeverities.Count
            || (_logsList.HasFocus && e.Row == _logsList.SelectedItem))
        {
            return;
        }
        if (RowColors.ForSeverity(_logSeverities[e.Row]) is { } color)
        {
            e.RowAttribute = Recolor(_logsList, new Color(color));
        }
    }

    // Same severity coloring as the per-trace Logs pane, for the global (all-services) stream.
    private void OnGlobalLogRowRender(object? sender, ListViewRowEventArgs e)
    {
        if (e.Row < 0 || e.Row >= _globalLogSeverities.Count
            || (_globalLogsList.HasFocus && e.Row == _globalLogsList.SelectedItem))
        {
            return;
        }
        if (RowColors.ForSeverity(_globalLogSeverities[e.Row]) is { } color)
        {
            e.RowAttribute = Recolor(_globalLogsList, new Color(color));
        }
    }

    // Color each waterfall row by span status (error spans red). Selected row keeps focus color.
    // Color each waterfall row by its span's service; an error span is red instead (status
    // takes precedence over service identity). Selected row keeps focus color while focused.
    private void OnWaterfallRowRender(object? sender, ListViewRowEventArgs e)
    {
        if (e.Row < 0 || e.Row >= _waterfallRows.Count
            || (_waterfall.HasFocus && e.Row == _waterfall.SelectedItem))
        {
            return;
        }
        var span = _waterfallRows[e.Row].Span;
        var color = RowColors.ForStatus(span.Status.Code) is { } error
            ? new Color(error)
            : _serviceColors.ColorFor(span.Service.Value);
        e.RowAttribute = Recolor(_waterfall, color);
    }

    // The list's normal attribute with only the foreground swapped, so the row keeps the
    // pane's background and just changes text color.
    private static TgAttribute Recolor(View list, Color foreground)
        => new(foreground, list.GetAttributeForRole(VisualRole.Normal).Background);

    // Loads (or live-refreshes) the selected trace's waterfall, details, and logs.
    // preserveSpan=false (a user picking a trace) resets the waterfall to the first span;
    // preserveSpan=true (a refresh tick) keeps the inspected span and skips the rebuild
    // entirely when nothing about the trace changed, so scroll/selection don't jump.
    private async Task SelectTraceAsync(
        TraceId id, CancellationToken cancellationToken, bool preserveSpan = false)
    {
        var trace = await _traceQueries.FindAsync(id, cancellationToken);
        if (trace is null)
        {
            return;
        }

        var rows = BuildWaterfallRows(trace);
        var logs = new List<LogRecord>();
        await foreach (var log in _logQueries.ForTraceAsync(id, cancellationToken))
        {
            logs.Add(log);
        }
        var newWaterfall = rows.Select(r => r.Display).ToList();
        var orderedLogs = logs.OrderBy(l => l.Timestamp).ToList();
        var newLogs = orderedLogs.Select(LogPresenter.Format).ToList();
        var newSeverities = orderedLogs.Select(l => l.Severity).ToList();

        TGuiApp.Invoke(() =>
        {
            if (preserveSpan
                // The user may have moved to a different trace before this query returned.
                && (CurrentSelectedTraceId() is not { } current || !current.Equals(id)))
            {
                return;
            }

            var waterfallChanged = !_waterfallText.SequenceEqual(newWaterfall, StringComparer.Ordinal);
            var logsChanged = !_logText.SequenceEqual(newLogs, StringComparer.Ordinal);

            // Nothing changed for this trace — leave both panes (and the user's place) be.
            if (preserveSpan && !waterfallChanged && !logsChanged)
            {
                return;
            }

            var previousSpan = preserveSpan ? SelectedSpanId() : null;
            _waterfallRows = rows;
            _selectedTraceLogs = logs;
            _waterfallText = newWaterfall;
            _logText = newLogs;
            _logSeverities = newSeverities;

            // Rebuild only the pane that actually changed: a fresh ListView source resets that
            // list's scroll offset, so re-sourcing an unchanged pane would jump it for no reason
            // (logs and spans arrive interleaved on a live trace). On a user-driven select
            // (preserveSpan=false) always rebuild both and reset to the first span.
            _suppressSelectionEvents = true;
            try
            {
                if (waterfallChanged || !preserveSpan)
                {
                    _waterfall.SetSource(new ObservableCollection<string>(newWaterfall));
                    if (rows.Count > 0)
                    {
                        _waterfall.SelectedItem =
                            ResolveSpanIndex(rows.Select(r => r.Span.SpanId).ToList(), previousSpan);
                    }
                }
                if (logsChanged || !preserveSpan)
                {
                    _logsList.SetSource(new ObservableCollection<string>(newLogs));
                }
            }
            finally
            {
                _suppressSelectionEvents = false;
            }

            // Always refresh Details: even when only logs changed, the correlated-logs section
            // shown for the (unchanged) selected span needs the new logs. Skip while Details is
            // pinned to command output (e.g. `:help`) so the tick doesn't clobber it.
            if (!_detailsPinned)
            {
                _details.Text = rows.Count > 0
                    ? RenderDetails(rows[_waterfall.SelectedItem ?? 0].Span, logs)
                    : "(no spans in trace)";
            }
        });
    }

    // The span id currently highlighted in the waterfall, or null if there is no valid selection.
    private SpanId? SelectedSpanId()
    {
        var index = _waterfall.SelectedItem;
        return index is { } i && i >= 0 && i < _waterfallRows.Count
            ? _waterfallRows[i].Span.SpanId
            : null;
    }

    // Index of the span matching `previous` (keep the user on it across a refresh), else 0 —
    // mirrors TraceSelection.ResolveIndex for the trace list.
    internal static int ResolveSpanIndex(IReadOnlyList<SpanId> spanIds, SpanId? previous)
    {
        if (previous is { } p)
        {
            for (var i = 0; i < spanIds.Count; i++)
            {
                if (spanIds[i].Equals(p))
                {
                    return i;
                }
            }
        }
        return 0;
    }

    private static List<WaterfallRow> BuildWaterfallRows(Trace trace)
    {
        // Cross-service assembly is a domain concern; the view only renders the result.
        var assembled = trace.Assemble();
        if (assembled.SpanCount == 0)
        {
            return [];
        }

        var (traceStart, traceEnd) = assembled.Envelope;
        var totalMs = Math.Max(1, (traceEnd - traceStart).TotalMilliseconds);

        var rows = new List<WaterfallRow>(assembled.SpanCount);
        foreach (var (node, depth) in assembled.Flatten())
        {
            var span = node.Span;
            var offsetPct = (span.StartTime - traceStart).TotalMilliseconds / totalMs;
            var widthPct = span.Duration.TotalMilliseconds / totalMs;
            var bar = BuildBar(offsetPct, widthPct, span.Status.Code);
            var indent = new string(' ', depth * 2);
            var display = $"{indent}{span.Service} · {span.Name,-28} {span.Duration.TotalMilliseconds,7:F1}ms  {bar}";
            rows.Add(new WaterfallRow(span, display));
        }
        return rows;
    }

    private static string BuildBar(double offsetPct, double widthPct, SpanStatusCode status)
    {
        const int barWidth = 24;
        var offset = (int)Math.Round(offsetPct * barWidth);
        var width = Math.Max(1, (int)Math.Round(widthPct * barWidth));
        offset = Math.Clamp(offset, 0, barWidth - 1);
        width = Math.Min(width, barWidth - offset);
        var fill = status == SpanStatusCode.Error ? '!' : '#';
        return $"[{new string(' ', offset)}{new string(fill, width)}{new string(' ', barWidth - offset - width)}]";
    }

    // Row text plus the column span of the status token, so TraceListSource can paint just the
    // token in its status color while the rest of the row uses the service color.
    private static (string Text, int StatusStart, int StatusLength) FormatTraceRow(TraceSummary t)
    {
        var prefix = $"{t.StartedAt:HH:mm:ss} ";
        var statusField = $"{t.StatusDisplay,-3}";
        var text = $"{prefix}{statusField} {t.ShortId} {t.RootService,-18} {t.RootName,-26} {t.DurationDisplay,8} ({t.SpanCount})";
        return (text, prefix.Length, statusField.Length);
    }

    private static string RenderDetails(Span span, IReadOnlyList<LogRecord> logs)
    {
        var spanLogs = logs.Where(l => l.SpanId.HasValue && l.SpanId.Value.Equals(span.SpanId)).ToList();

        var lines = new List<string>();

        // Lead with the failure when there is one: error status message + exception.* details.
        var spotlight = ErrorSpotlight.For(span);
        if (spotlight.Count > 0)
        {
            lines.AddRange(spotlight);
            lines.Add(string.Empty);
        }

        lines.AddRange(
        [
            $"name        {span.Name}",
            $"span_id     {span.SpanId}",
            $"trace_id    {span.TraceId}",
            $"parent      {span.ParentSpanId?.ToString() ?? "(none)"}",
            $"kind        {span.Kind}",
            $"status      {span.Status.Code}{(span.Status.Description is null ? "" : $" — {span.Status.Description}")}",
            $"start       {span.StartTime:HH:mm:ss.fff}",
            $"duration    {span.Duration.TotalMilliseconds:F2} ms",
        ]);

        // The producer (OTLP Resource): the emitting service plus folded resource.* attributes.
        var resourceAttrs = span.Attributes
            .Where(kv => kv.Key.StartsWith("resource.", StringComparison.Ordinal))
            .ToList();
        var ownAttrs = span.Attributes
            .Where(kv => !kv.Key.StartsWith("resource.", StringComparison.Ordinal))
            .ToList();

        lines.Add(string.Empty);
        lines.Add("producer");
        lines.Add($"  service = {span.Service}");
        foreach (var (key, value) in resourceAttrs)
        {
            lines.Add($"  {key["resource.".Length..]} = {AttributeText.Render(value)}");
        }

        lines.Add(string.Empty);
        lines.Add($"attributes  ({ownAttrs.Count})");
        foreach (var (key, value) in ownAttrs)
        {
            lines.Add($"  {key} = {AttributeText.Render(value)}");
        }

        if (span.Events.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"events      ({span.Events.Count})");
            foreach (var spanEvent in span.Events)
            {
                lines.Add($"  {spanEvent.Timestamp:HH:mm:ss.fff} {spanEvent.Name}");
                foreach (var (key, value) in spanEvent.Attributes)
                {
                    lines.Add($"    {key} = {AttributeText.Render(value)}");
                }
            }
        }

        if (span.Links.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"links       ({span.Links.Count})");
            foreach (var link in span.Links)
            {
                lines.Add($"  {link.TraceId.Value[..16]}… / {link.SpanId}");
            }
        }

        lines.Add(string.Empty);
        lines.Add($"correlated logs ({spanLogs.Count})");
        foreach (var log in spanLogs)
        {
            lines.Add($"  {log.Timestamp:HH:mm:ss.fff} {log.Severity,-5} {log.Body}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private readonly record struct WaterfallRow(Span Span, string Display);
}
