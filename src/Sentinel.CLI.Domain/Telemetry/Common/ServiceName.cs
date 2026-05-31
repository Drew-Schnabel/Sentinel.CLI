namespace Sentinel.CLI.Domain.Telemetry.Common;

public readonly record struct ServiceName
{
    public string Value { get; }

    private ServiceName(string value) => Value = value;

    public static ServiceName From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var trimmed = value.Trim();
        if (trimmed.Length > 255)
        {
            throw new ArgumentException("service.name exceeds 255 characters.", nameof(value));
        }
        return new ServiceName(trimmed);
    }

    public override string ToString() => Value;
}
