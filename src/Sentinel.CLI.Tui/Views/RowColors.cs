using Sentinel.CLI.Domain.Telemetry.Logs;
using Sentinel.CLI.Domain.Telemetry.Spans;
using Terminal.Gui.Drawing;

namespace Sentinel.CLI.Tui.Views;

// Maps a log severity / span status to the foreground color its row should use, or null to
// leave the row at the list's default scheme color. Pure so the choices are unit-testable
// without a terminal; the view glue turns the color name into an Attribute and applies it.
internal static class RowColors
{
    public static ColorName16? ForSeverity(LogSeverity severity) => severity switch
    {
        LogSeverity.Fatal or LogSeverity.Error => ColorName16.BrightRed,
        LogSeverity.Warn => ColorName16.BrightYellow,
        LogSeverity.Trace or LogSeverity.Debug => ColorName16.DarkGray,
        _ => null, // Info / unspecified — keep the default color
    };

    public static ColorName16? ForStatus(SpanStatusCode status) =>
        status == SpanStatusCode.Error ? ColorName16.BrightRed : null;

    // Color for the status token shown in a trace row (the rest of the row is service-colored):
    // error red, ok green, anything else (unset) grey.
    public static ColorName16 StatusToken(SpanStatusCode status) => status switch
    {
        SpanStatusCode.Error => ColorName16.BrightRed,
        SpanStatusCode.Ok => ColorName16.BrightGreen,
        _ => ColorName16.Gray,
    };
}

