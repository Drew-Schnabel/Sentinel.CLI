using Terminal.Gui.Drawing;

namespace Sentinel.CLI.Tui.Views;

// Assigns each service a soft, distinct foreground color. Colors are handed out in first-seen
// order from a muted palette so distinct services are reliably distinguishable (a hash into the
// palette would collide for a handful of services, which defeats the point). Assignments are
// remembered for the window's lifetime, so a service keeps its color across refreshes. Once the
// palette is exhausted, colors repeat. Red is excluded — that's reserved for error status.
internal sealed class ServiceColorMap
{
    // Medium-brightness, low-saturation tones: visible on a dark terminal without the harshness
    // of the bright ANSI set, and distinct in hue from one another. The default (dark) palette.
    public static readonly IReadOnlyList<Color> DarkPalette =
    [
        new(97, 150, 220),  // periwinkle
        new(140, 195, 125), // sage
        new(210, 170, 95),  // amber
        new(190, 135, 195), // orchid
        new(95, 180, 175),  // teal
        new(170, 165, 210), // lavender
        new(215, 150, 120), // peach
        new(175, 185, 115), // olive
    ];

    private readonly IReadOnlyList<Color> _palette;
    private readonly Dictionary<string, Color> _assigned = new(StringComparer.Ordinal);
    private int _next;

    public ServiceColorMap() : this(DarkPalette)
    {
    }

    public ServiceColorMap(IReadOnlyList<Color> palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (palette.Count == 0)
        {
            throw new ArgumentException("Service palette must contain at least one color.", nameof(palette));
        }
        _palette = palette;
    }

    public Color ColorFor(string service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (!_assigned.TryGetValue(service, out var color))
        {
            color = _palette[_next % _palette.Count];
            _assigned[service] = color;
            _next++;
        }
        return color;
    }
}
