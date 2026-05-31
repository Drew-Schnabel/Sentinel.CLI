using Sentinel.CLI.Domain.Telemetry.Logs;

namespace Sentinel.CLI.Tui.Views;

// Formats a log record for the Logs pane. Pure so it can be unit-tested without the TUI.
internal static class LogPresenter
{
    public static string Format(LogRecord log)
    {
        ArgumentNullException.ThrowIfNull(log);

        // Short span id marks which span a log correlates to; absent for uncorrelated logs.
        var tag = log.SpanId is { } spanId ? $"[{spanId.Value[..8]}] " : string.Empty;
        return $"{log.Timestamp:HH:mm:ss.fff} {SeverityLabel(log.Severity),-5} {tag}{log.Body}";
    }

    // For the global (all-services) stream, where the emitting service matters and the row isn't
    // scoped to one trace. Includes the service column instead of the span-correlation tag.
    public static string FormatWithService(LogRecord log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return $"{log.Timestamp:HH:mm:ss.fff} {SeverityLabel(log.Severity),-5} {log.Service.Value,-20} {log.Body}";
    }

    public static string SeverityLabel(LogSeverity severity) => severity switch
    {
        LogSeverity.Trace => "TRACE",
        LogSeverity.Debug => "DEBUG",
        LogSeverity.Info => "INFO",
        LogSeverity.Warn => "WARN",
        LogSeverity.Error => "ERROR",
        LogSeverity.Fatal => "FATAL",
        _ => "-",
    };
}
