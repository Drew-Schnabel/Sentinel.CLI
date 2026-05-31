using System.ComponentModel.DataAnnotations;

namespace Sentinel.CLI.Tui;

public sealed class TuiOptions
{
    public const string SectionName = "Tui";

    // How often the TUI polls the store to pull in newly received telemetry and live-update the
    // open trace's panes. Lower = snappier but more redraw churn. Bound from "Tui:RefreshMs".
    [Range(100, 60_000)]
    public int RefreshMs { get; set; } = 1_000;

    public TimeSpan RefreshInterval => TimeSpan.FromMilliseconds(RefreshMs);

    // Color theme name (dark | light | high-contrast | colorblind). Bound from "Tui:Theme"
    // (env "Tui__Theme"). Deliberately NOT validated here — an unrecognized name falls back to the
    // default theme at startup rather than crashing the app. Runtime `:theme <name>` overrides it.
    public string Theme { get; set; } = "dark";
}
