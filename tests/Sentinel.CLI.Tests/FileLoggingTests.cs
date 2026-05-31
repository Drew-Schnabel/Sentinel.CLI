using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sentinel.CLI.Logging;

namespace Sentinel.CLI.Tests;

public class FileLoggingTests
{
    private static readonly DateTimeOffset T = new(2024, 1, 1, 8, 30, 15, 123, TimeSpan.Zero);

    private static IConfiguration Config(params (string Key, string? Value)[] pairs)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    [Fact]
    public void Compose_includes_timestamp_level_category_and_message()
        => FileLogFormat.Compose(T, LogLevel.Warning, "Kestrel", "port busy", exception: null)
            .Should().Contain("2024-01-01 08:30:15.123")
            .And.Contain("[WRN]").And.Contain("Kestrel").And.Contain("port busy");

    [Fact]
    public void Compose_appends_the_exception()
        => FileLogFormat.Compose(T, LogLevel.Error, "c", "boom", new InvalidOperationException("nope"))
            .Should().Contain("nope");

    [Theory]
    [InlineData(LogLevel.Trace, "TRC")]
    [InlineData(LogLevel.Information, "INF")]
    [InlineData(LogLevel.Warning, "WRN")]
    [InlineData(LogLevel.Critical, "CRT")]
    public void Level_tag_per_level(LogLevel level, string expected)
        => FileLogFormat.Level(level).Should().Be(expected);

    [Fact]
    public void Provider_writes_enabled_levels_and_filters_below_the_minimum()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sentinel-test-{Guid.NewGuid():N}.log");
        try
        {
            using (var provider = new FileLoggerProvider(path, LogLevel.Warning))
            {
                var logger = provider.CreateLogger("Test");
                // Call ILogger.Log directly (not the LogInformation/LogWarning extensions) to
                // keep the analyzer's LoggerMessage-delegate rule out of test code.
                logger.Log(LogLevel.Information, default, "info-skipped", null, static (s, _) => s);
                logger.Log(LogLevel.Warning, default, "warn-kept", null, static (s, _) => s);
            }

            var text = File.ReadAllText(path);
            text.Should().Contain("warn-kept").And.NotContain("info-skipped");
            text.Should().Contain("session started"); // the startup marker line
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResolveMinLevel_defaults_to_warning()
        => FileLogging.ResolveMinLevel(Config()).Should().Be(LogLevel.Warning);

    [Fact]
    public void ResolveMinLevel_honors_configuration()
        => FileLogging.ResolveMinLevel(Config(("Logging:File:LogLevel", "Debug")))
            .Should().Be(LogLevel.Debug);

    [Fact]
    public void ResolvePath_uses_the_configured_path_when_set()
        => FileLogging.ResolvePath(Config(("Logging:File:Path", "/tmp/custom.log")))
            .Should().Be("/tmp/custom.log");

    [Fact]
    public void ResolvePath_defaults_under_local_app_data()
        => FileLogging.ResolvePath(Config()).Should()
            .Contain("Sentinel.CLI").And.EndWith(".log");
}
