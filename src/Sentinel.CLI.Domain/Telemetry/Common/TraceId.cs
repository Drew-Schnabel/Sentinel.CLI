using System.Text.RegularExpressions;

namespace Sentinel.CLI.Domain.Telemetry.Common;

public readonly partial record struct TraceId
{
    private static readonly Regex HexPattern = HexRegex();

    public string Value { get; }

    private TraceId(string value) => Value = value;

    public static TraceId Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!HexPattern.IsMatch(value))
        {
            throw new FormatException($"TraceId must be 32 lowercase hex characters; got '{value}'.");
        }
        if (value == "00000000000000000000000000000000")
        {
            throw new FormatException("TraceId must not be all zeros.");
        }
        return new TraceId(value);
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexRegex();
}
