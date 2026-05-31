using Terminal.Gui.Drawing;

namespace Sentinel.CLI.Tui.Views;

// The built-in themes and a pure name → theme resolver. Resolution is case-insensitive and total
// (returns null for an unknown name); callers fall back to Default rather than throwing.
internal static class Themes
{
    public static Theme Default => Dark;

    // Light-gray on near-black; the muted palette tuned for dark terminals.
    public static Theme Dark { get; } = new(
        ThemeName.Dark,
        foreground: new Color(208, 208, 208),
        background: new Color(16, 16, 16),
        focusForeground: new Color(255, 255, 255),
        focusBackground: new Color(40, 70, 120),
        servicePalette: ServiceColorMap.DarkPalette);

    // Near-black on near-white, with darker, saturated service colors that read on a light bg.
    public static Theme Light { get; } = new(
        ThemeName.Light,
        foreground: new Color(30, 30, 30),
        background: new Color(245, 245, 245),
        focusForeground: new Color(255, 255, 255),
        focusBackground: new Color(40, 90, 170),
        servicePalette:
        [
            new Color(30, 90, 175),   // blue
            new Color(35, 120, 60),   // green
            new Color(150, 95, 20),   // brown/amber
            new Color(130, 60, 150),  // purple
            new Color(20, 110, 110),  // teal
            new Color(90, 80, 170),   // indigo
            new Color(160, 70, 60),   // rust
            new Color(110, 110, 30),  // olive
        ]);

    // Maximum legibility: pure white on pure black, bright distinct service colors.
    public static Theme HighContrast { get; } = new(
        ThemeName.HighContrast,
        foreground: new Color(255, 255, 255),
        background: new Color(0, 0, 0),
        focusForeground: new Color(0, 0, 0),
        focusBackground: new Color(255, 255, 255),
        servicePalette:
        [
            new Color(80, 170, 255),  // bright blue
            new Color(80, 230, 120),  // bright green
            new Color(255, 215, 70),  // bright yellow
            new Color(230, 130, 240), // bright magenta
            new Color(90, 230, 230),  // bright cyan
            new Color(255, 170, 90),  // bright orange
            new Color(200, 200, 200), // light gray
            new Color(170, 230, 110), // lime
        ]);

    // Colorblind-safe service palette (Okabe–Ito), on a dark bg. Service distinction is the win
    // here; status still uses RowColors (text + fill mitigate the green/red trap).
    public static Theme Colorblind { get; } = new(
        ThemeName.Colorblind,
        foreground: new Color(220, 220, 220),
        background: new Color(16, 16, 16),
        focusForeground: new Color(0, 0, 0),
        focusBackground: new Color(230, 200, 90),
        servicePalette:
        [
            new Color(0, 114, 178),   // blue
            new Color(230, 159, 0),   // orange
            new Color(86, 180, 233),  // sky blue
            new Color(0, 158, 115),   // bluish green
            new Color(240, 228, 66),  // yellow
            new Color(213, 94, 0),    // vermillion
            new Color(204, 121, 167), // reddish purple
            new Color(153, 153, 153), // gray
        ],
        // Break the OK/ERR green↔red trap: OK in blue, ERR in vermillion (both Okabe–Ito, mutually
        // distinguishable for the common color-vision deficiencies).
        okStatus: new Color(0, 114, 178),     // blue (not green)
        errorStatus: new Color(213, 94, 0),   // vermillion
        unsetStatus: new Color(153, 153, 153)); // gray

    public static Theme? Resolve(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "dark" => Dark,
        "light" => Light,
        "high-contrast" or "highcontrast" or "contrast" => HighContrast,
        "colorblind" or "color-blind" or "cb" => Colorblind,
        _ => null,
    };

    // The accepted names, for the `:theme` error message and help.
    public static string Names => "dark, light, high-contrast, colorblind";
}
