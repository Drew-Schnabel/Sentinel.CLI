using System.Globalization;
using Sentinel.CLI.Domain.Telemetry.Common;

namespace Sentinel.CLI.Tui.Views;

// Shared rendering of an attribute value to a display string.
internal static class AttributeText
{
    public static string Render(AttributeValue value) => value switch
    {
        AttributeValue.Text t => t.Value,
        AttributeValue.Integer i => i.Value.ToString(CultureInfo.InvariantCulture),
        AttributeValue.Number n => n.Value.ToString(CultureInfo.InvariantCulture),
        AttributeValue.Flag f => f.Value ? "true" : "false",
        AttributeValue.TextList l => $"[{string.Join(", ", l.Values)}]",
        _ => "(unknown)",
    };
}
