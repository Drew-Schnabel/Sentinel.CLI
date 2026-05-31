// OTel AnyValue mirrors primitive type names by design; analyzer naming rules don't apply.
#pragma warning disable CA1716, CA1720

namespace Sentinel.CLI.Domain.Telemetry.Common;

public abstract record AttributeValue
{
    public sealed record Text(string Value) : AttributeValue;
    public sealed record Integer(long Value) : AttributeValue;
    public sealed record Number(double Value) : AttributeValue;
    public sealed record Flag(bool Value) : AttributeValue;
    public sealed record TextList(IReadOnlyList<string> Values) : AttributeValue;
}
