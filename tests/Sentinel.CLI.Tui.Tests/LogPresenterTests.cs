using FluentAssertions;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Logs;
using Sentinel.CLI.Tui.Views;

namespace Sentinel.CLI.Tui.Tests;

public class LogPresenterTests
{
    private static readonly DateTimeOffset T = new(2024, 1, 1, 8, 30, 15, 123, TimeSpan.Zero);

    [Theory]
    [InlineData(LogSeverity.Trace, "TRACE")]
    [InlineData(LogSeverity.Debug, "DEBUG")]
    [InlineData(LogSeverity.Info, "INFO")]
    [InlineData(LogSeverity.Warn, "WARN")]
    [InlineData(LogSeverity.Error, "ERROR")]
    [InlineData(LogSeverity.Fatal, "FATAL")]
    [InlineData(LogSeverity.Unspecified, "-")]
    public void SeverityLabel_maps_each_level(LogSeverity severity, string expected)
        => LogPresenter.SeverityLabel(severity).Should().Be(expected);

    [Fact]
    public void Format_includes_severity_and_body()
    {
        var log = LogRecord.Create(T, LogSeverity.Error, ServiceName.From("svc"), "boom");

        LogPresenter.Format(log).Should().Contain("ERROR").And.Contain("boom");
    }

    [Fact]
    public void Format_tags_the_correlated_span_with_its_short_id()
    {
        var log = LogRecord.Create(
            T, LogSeverity.Info, ServiceName.From("svc"), "hi",
            TraceId.Parse("4bf92f3577b34da6a3ce929d0e0e4736"),
            SpanId.Parse("00f067aa0ba902b7"));

        LogPresenter.Format(log).Should().Contain("[00f067aa]");
    }

    [Fact]
    public void Format_without_a_span_has_no_correlation_tag()
    {
        var log = LogRecord.Create(T, LogSeverity.Info, ServiceName.From("svc"), "hi");

        LogPresenter.Format(log).Should().NotContain("[");
    }

    [Fact]
    public void FormatWithService_includes_the_service_severity_and_body()
    {
        var log = LogRecord.Create(T, LogSeverity.Warn, ServiceName.From("orders-api"), "slow");

        LogPresenter.FormatWithService(log).Should()
            .Contain("WARN").And.Contain("orders-api").And.Contain("slow");
    }
}
