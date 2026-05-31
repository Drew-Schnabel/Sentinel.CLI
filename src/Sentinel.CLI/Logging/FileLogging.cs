using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Sentinel.CLI.Logging;

// Console logging is cleared at startup because it would paint over the Terminal.Gui screen
// (and, in --server mode, there's still value in a durable record). This routes warnings/errors
// to a file instead so failures stay diagnosable. Minimal by design — no third-party logging
// dependency.
internal static class FileLogging
{
    // Adds the file sink (best-effort) and returns the resolved path, or null if the file could
    // not be opened (a logging-setup failure must never take down the app).
    public static string? Configure(ILoggingBuilder logging, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(logging);
        ArgumentNullException.ThrowIfNull(configuration);
        var path = ResolvePath(configuration);
        var minLevel = ResolveMinLevel(configuration);
        try
        {
            logging.AddProvider(new FileLoggerProvider(path, minLevel));
            return path;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static string ResolvePath(IConfiguration configuration)
    {
        var configured = configuration["Logging:File:Path"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sentinel.CLI",
            "logs");
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return Path.Combine(dir, $"sentinel-{stamp}.log");
    }

    // Default Warning: capture the failures worth diagnosing without per-request hosting/Kestrel
    // Information spam. Lower it via Logging:File:LogLevel when more detail is wanted.
    internal static LogLevel ResolveMinLevel(IConfiguration configuration)
        => Enum.TryParse<LogLevel>(configuration["Logging:File:LogLevel"], ignoreCase: true, out var level)
            ? level
            : LogLevel.Warning;
}

// Pure formatting of a single log line, so the wire format is unit-testable without file I/O.
internal static class FileLogFormat
{
    public static string Level(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    public static string Compose(
        DateTimeOffset timestamp, LogLevel level, string category, string message, Exception? exception)
    {
        var line = new StringBuilder()
            .Append(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(" [").Append(Level(level)).Append("] ")
            .Append(category).Append(": ").Append(message);
        if (exception is not null)
        {
            line.Append(" | ").Append(exception);
        }
        return line.ToString();
    }
}

internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly Lock _gate = new();
    private readonly StreamWriter _writer;
    private readonly LogLevel _minLevel;

    public FileLoggerProvider(string path, LogLevel minLevel)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        _writer = new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        _minLevel = minLevel;
        // A session marker so a multi-run append-log is readable; bypasses the level filter.
        var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        lock (_gate)
        {
            _writer.WriteLine($"=== Sentinel.CLI session started {stamp} ===");
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _minLevel, _gate, _writer);

    public void Dispose()
    {
        lock (_gate)
        {
            _writer.Dispose();
        }
    }

    private sealed class FileLogger(string category, LogLevel minLevel, Lock gate, TextWriter writer) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= minLevel;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
            {
                return;
            }
            var line = FileLogFormat.Compose(
                DateTimeOffset.Now, logLevel, category, formatter(state, exception), exception);
            lock (gate)
            {
                writer.WriteLine(line);
            }
        }
    }
}
