using System.Globalization;

namespace Sentinel.CLI.Tui.Views;

// Parses a short human duration like "30s", "5m", "2h", "500ms", "1d" into a TimeSpan. Total —
// returns null for anything malformed (no number, unknown/missing unit, negative). Used by the
// `:filter since=…` time window.
internal static class DurationParse
{
    public static TimeSpan? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var s = text.Trim().ToLowerInvariant();

        var i = 0;
        while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '.'))
        {
            i++;
        }
        if (i == 0 || i == s.Length) // no number, or no unit
        {
            return null;
        }

        if (!double.TryParse(s[..i], NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            return null;
        }

        return s[i..] switch
        {
            "ms" => TimeSpan.FromMilliseconds(value),
            "s" => TimeSpan.FromSeconds(value),
            "m" => TimeSpan.FromMinutes(value),
            "h" => TimeSpan.FromHours(value),
            "d" => TimeSpan.FromDays(value),
            _ => null,
        };
    }
}
