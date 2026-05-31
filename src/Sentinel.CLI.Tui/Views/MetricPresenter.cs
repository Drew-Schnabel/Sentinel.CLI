using System.Globalization;
using Sentinel.CLI.Domain.Telemetry.Metrics;

namespace Sentinel.CLI.Tui.Views;

// Pure formatting of a metric point for the metrics table. Unit-tested without the TUI.
internal static class MetricPresenter
{
    public static string FormatValue(MetricPoint metric) => metric.Kind == MetricKind.Histogram
        ? $"count={metric.Count} sum={Num(metric.Sum)} min={Num(metric.Min)} max={Num(metric.Max)}"
        : Num(metric.Value);

    public static string FormatLabels(MetricPoint metric)
        => string.Join(", ", metric.Labels.Select(kv => $"{kv.Key}={AttributeText.Render(kv.Value)}"));

    // The single number to trend in a sparkline: the value for gauge/sum; for a histogram the
    // mean (sum/count) when available, else the count.
    public static double SparkValue(MetricPoint metric)
    {
        ArgumentNullException.ThrowIfNull(metric);
        if (metric.Kind != MetricKind.Histogram)
        {
            return metric.Value ?? 0;
        }
        return metric is { Count: > 0, Sum: { } sum } ? sum / metric.Count.Value : metric.Count ?? 0;
    }

    private static string Num(double? value)
        => value?.ToString("G", CultureInfo.InvariantCulture) ?? "-";
}
