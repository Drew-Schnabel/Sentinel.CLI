// The static Application facade is marked obsolete ("going away"), but in Terminal.Gui 2.4.3 it
// is the path that actually receives keyboard input — the newer Application.Create() instance
// renders and runs timers but does not deliver key events. Use the static facade until the
// instance API gains input; the same suppression is already used for TextView elsewhere.
#pragma warning disable CS0618

using Microsoft.Extensions.Options;
using Sentinel.CLI.Application.Telemetry.Diagnostics;
using Sentinel.CLI.Application.Telemetry.Ports;
using Sentinel.CLI.Application.Telemetry.Queries;
using Sentinel.CLI.Tui.Views;
using TGui = Terminal.Gui.App.Application;

namespace Sentinel.CLI.Tui;

public sealed class TuiRunner
{
    private readonly ITraceQueries _traceQueries;
    private readonly ILogQueries _logQueries;
    private readonly IMetricQueries _metricQueries;
    private readonly ITelemetryStats _stats;
    private readonly IIngestDiagnostics _diagnostics;
    private readonly IStoreControl _storeControl;

    private readonly TimeSpan _refreshInterval;
    private readonly string _themeName;

    private MainWindow? _runningWindow;

    public TuiRunner(
        ITraceQueries traceQueries,
        ILogQueries logQueries,
        IMetricQueries metricQueries,
        ITelemetryStats stats,
        IIngestDiagnostics diagnostics,
        IStoreControl storeControl,
        IOptions<TuiOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _traceQueries = traceQueries;
        _logQueries = logQueries;
        _metricQueries = metricQueries;
        _stats = stats;
        _diagnostics = diagnostics;
        _storeControl = storeControl;
        _refreshInterval = options.Value.RefreshInterval;
        _themeName = options.Value.Theme;
    }

    public void Run()
    {
        // Use the static Application facade: Terminal.Gui pumps keyboard input to the global
        // application, so a standalone Application.Create() instance (the original spike) never
        // received keys even though it rendered and ran timers.
        TGui.Init();
        try
        {
            using var window = new MainWindow(_traceQueries, _logQueries, _metricQueries, _stats, _diagnostics, _storeControl);
            window.BindKeys(); // wire shortcuts now that the keyboard subsystem is live
            window.ApplyTheme(Themes.Resolve(_themeName) ?? Themes.Default); // configured theme (lenient)

            _runningWindow = window;
            object? refreshTimer = null;
            try
            {
                _ = window.LoadAsync(CancellationToken.None);
                // Poll the store so telemetry received after launch appears without user action.
                refreshTimer = TGui.AddTimeout(_refreshInterval, () =>
                {
                    _ = window.RefreshAsync(CancellationToken.None);
                    return true; // reschedule
                });
                TGui.Run(window);
            }
            finally
            {
                if (refreshTimer is not null)
                {
                    TGui.RemoveTimeout(refreshTimer);
                }
                _runningWindow = null;
            }
        }
        finally
        {
            TGui.Shutdown();
        }
    }

    // Breaks the blocking Run() loop from another thread (e.g. a SIGTERM handler) so the
    // host's StopAsync runs and Terminal.Gui restores the terminal. No-op if not running.
    public void RequestStop()
    {
        var window = _runningWindow;
        if (window is null)
        {
            return;
        }
        TGui.Invoke(() => TGui.RequestStop(window));
    }
}
