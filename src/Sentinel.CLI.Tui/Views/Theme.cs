using Sentinel.CLI.Domain.Telemetry.Spans;
using Terminal.Gui.Drawing;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Sentinel.CLI.Tui.Views;

internal enum ThemeName
{
    Dark,
    Light,
    HighContrast,
    Colorblind,
}

// A color theme: the base scheme colors (window foreground/background + focus) plus the per-service
// palette. Built so `:theme` can swap the whole look at runtime. Status/severity colors still come
// from RowColors for now (status is also signalled by the OK/ERR text token and the !/# waterfall
// fill, so it isn't color-only) — colorblind-specific status colors are a follow-up.
internal sealed class Theme
{
    // Optional per-status overrides for the trace-list status token (OK/ERR/unset). Null → use the
    // shared RowColors default. Only the colorblind theme sets these, to break the green/red trap.
    private readonly Color? _okStatus;
    private readonly Color? _errorStatus;
    private readonly Color? _unsetStatus;

    public Theme(
        ThemeName name,
        Color foreground,
        Color background,
        Color focusForeground,
        Color focusBackground,
        IReadOnlyList<Color> servicePalette,
        Color? okStatus = null,
        Color? errorStatus = null,
        Color? unsetStatus = null)
    {
        ArgumentNullException.ThrowIfNull(servicePalette);
        Name = name;
        Foreground = foreground;
        Background = background;
        FocusForeground = focusForeground;
        FocusBackground = focusBackground;
        ServicePalette = servicePalette;
        _okStatus = okStatus;
        _errorStatus = errorStatus;
        _unsetStatus = unsetStatus;
    }

    public ThemeName Name { get; }
    public Color Foreground { get; }
    public Color Background { get; }
    public Color FocusForeground { get; }
    public Color FocusBackground { get; }
    public IReadOnlyList<Color> ServicePalette { get; }

    // Color for the trace-list status token (OK/ERR/—). Falls back to the shared RowColors default
    // (green/red/grey) unless the theme overrides it (colorblind uses blue/vermillion to avoid the
    // green/red trap). Status is also shown as a text token, so it's never color-only.
    public Color StatusTokenColor(SpanStatusCode code) => code switch
    {
        SpanStatusCode.Ok => _okStatus ?? new Color(RowColors.StatusToken(code)),
        SpanStatusCode.Error => _errorStatus ?? new Color(RowColors.StatusToken(code)),
        _ => _unsetStatus ?? new Color(RowColors.StatusToken(code)),
    };

    // A Terminal.Gui Scheme covering the roles the app uses. Hot* mirror their base role (no mnemonics).
    public Scheme BuildScheme()
    {
        var normal = new TgAttribute(Foreground, Background);
        var focus = new TgAttribute(FocusForeground, FocusBackground);
        return new Scheme(normal)
        {
            Normal = normal,
            HotNormal = normal,
            Focus = focus,
            HotFocus = focus,
        };
    }
}
