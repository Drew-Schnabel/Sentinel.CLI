using System.Text;

namespace Sentinel.CLI.Tui.Views;

// Renders a sequence of values as a one-line bar chart using block glyphs, normalized to the
// run's own min..max. Pure, so it's unit-testable without a terminal. One glyph per value.
internal static class Sparkline
{
    private const string Bars = "▁▂▃▄▅▆▇█"; // eight levels, low → high

    public static string Render(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return string.Empty;
        }

        var min = values[0];
        var max = values[0];
        foreach (var v in values)
        {
            if (v < min) { min = v; }
            if (v > max) { max = v; }
        }
        var range = max - min;

        var sb = new StringBuilder(values.Count);
        foreach (var v in values)
        {
            // A flat run carries no relative info — show a steady mid bar rather than implying
            // zero. Otherwise scale into 0..7.
            var level = range <= 0
                ? (Bars.Length - 1) / 2
                : Math.Clamp((int)Math.Round((v - min) / range * (Bars.Length - 1)), 0, Bars.Length - 1);
            sb.Append(Bars[level]);
        }
        return sb.ToString();
    }
}
