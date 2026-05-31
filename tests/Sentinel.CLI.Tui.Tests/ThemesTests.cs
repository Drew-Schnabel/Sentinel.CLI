using FluentAssertions;
using Sentinel.CLI.Domain.Telemetry.Spans;
using Sentinel.CLI.Tui.Views;
using Terminal.Gui.Drawing;

namespace Sentinel.CLI.Tui.Tests;

public class ThemesTests
{
    // expected passed as a string (the enum name) — ThemeName is internal and can't appear in a
    // public [Theory] signature.
    [Theory]
    [InlineData("dark", "Dark")]
    [InlineData("Dark", "Dark")]
    [InlineData("light", "Light")]
    [InlineData("LIGHT", "Light")]
    [InlineData("high-contrast", "HighContrast")]
    [InlineData("highcontrast", "HighContrast")]
    [InlineData("colorblind", "Colorblind")]
    [InlineData("cb", "Colorblind")]
    public void Resolve_maps_known_names_case_insensitively(string name, string expected)
        => Themes.Resolve(name)!.Name.ToString().Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    [InlineData("solarized")]
    public void Resolve_returns_null_for_unknown_or_blank_names(string? name)
        => Themes.Resolve(name).Should().BeNull();

    [Fact]
    public void Default_is_dark()
        => Themes.Default.Name.Should().Be(ThemeName.Dark);

    [Fact]
    public void Light_and_dark_have_different_backgrounds()
        => Themes.Light.Background.Should().NotBe(Themes.Dark.Background);

    [Fact]
    public void Every_theme_has_a_non_empty_palette_of_distinct_colors()
    {
        foreach (var theme in new[] { Themes.Dark, Themes.Light, Themes.HighContrast, Themes.Colorblind })
        {
            theme.ServicePalette.Should().NotBeEmpty();
            theme.ServicePalette.Should().OnlyHaveUniqueItems(); // distinct so services are distinguishable
        }
    }

    [Fact]
    public void Colorblind_status_tokens_break_the_green_red_trap()
    {
        var ok = Themes.Colorblind.StatusTokenColor(SpanStatusCode.Ok);
        var err = Themes.Colorblind.StatusTokenColor(SpanStatusCode.Error);

        ok.Should().NotBe(err); // OK and ERR are mutually distinguishable…
        ok.Should().NotBe(new Color(RowColors.StatusToken(SpanStatusCode.Ok))); // …and OK isn't the default green
    }

    [Fact]
    public void Default_themes_keep_the_shared_status_token_colors()
    {
        foreach (var theme in new[] { Themes.Dark, Themes.Light, Themes.HighContrast })
        {
            theme.StatusTokenColor(SpanStatusCode.Ok)
                .Should().Be(new Color(RowColors.StatusToken(SpanStatusCode.Ok)));
            theme.StatusTokenColor(SpanStatusCode.Error)
                .Should().Be(new Color(RowColors.StatusToken(SpanStatusCode.Error)));
        }
    }

    [Fact]
    public void BuildScheme_uses_the_theme_foreground_and_background_for_normal()
    {
        var scheme = Themes.Light.BuildScheme();

        scheme.Normal.Background.Should().Be(Themes.Light.Background);
        scheme.Normal.Foreground.Should().Be(Themes.Light.Foreground);
        scheme.Focus.Background.Should().Be(Themes.Light.FocusBackground);
    }
}
