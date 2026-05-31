using System.Collections;

namespace Sentinel.CLI.Domain.Telemetry.Common;

public sealed class TelemetryAttributes : IEnumerable<KeyValuePair<string, AttributeValue>>
{
    public static TelemetryAttributes Empty { get; } = new(new Dictionary<string, AttributeValue>(0));

    private readonly Dictionary<string, AttributeValue> _values;

    private TelemetryAttributes(Dictionary<string, AttributeValue> values) => _values = values;

    public static TelemetryAttributes From(IEnumerable<KeyValuePair<string, AttributeValue>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = new Dictionary<string, AttributeValue>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);
            copy[key] = value;
        }
        return copy.Count == 0 ? Empty : new TelemetryAttributes(copy);
    }

    public int Count => _values.Count;

    public bool TryGet(string key, out AttributeValue value) => _values.TryGetValue(key, out value!);

    public IEnumerator<KeyValuePair<string, AttributeValue>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
