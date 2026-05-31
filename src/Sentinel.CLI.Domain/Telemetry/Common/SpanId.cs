using System.Text.RegularExpressions;

namespace Sentinel.CLI.Domain.Telemetry.Common;

public readonly partial record struct SpanId
{
    private static readonly Regex HexPattern = HexRegex();

    public string Value { get; }

    private SpanId(string value) => Value = value;

    public static SpanId Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!HexPattern.IsMatch(value))
        {
            throw new FormatException($"SpanId must be 16 lowercase hex characters; got '{value}'.");
        }
        if (value == "0000000000000000")
        {
            throw new FormatException("SpanId must not be all zeros.");
        }
        return new SpanId(value);
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[0-9a-f]{16}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexRegex();
}
