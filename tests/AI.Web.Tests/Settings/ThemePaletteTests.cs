namespace AI.Web.Tests.Settings;

using AI.Contracts.Themes;
using Shouldly;
using Xunit;

/// <summary>
/// The engine has one hard promise: a theme built from seeds carries what the stylesheet carries. The
/// presets in app.css are the reference, so the darkest preset is rebuilt from its single background
/// colour and compared with itself — a regressed ramp, a swapped side or a missed role shows up as a
/// number that moved.
/// </summary>
public sealed class ThemePaletteTests
{
    [Fact]
    public void ShouldRebuildTheDarkPresetFromItsOwnBackground()
    {
        var tokens = ThemePalette.Generate(new CustomThemeSeeds("#171717")).Tokens;

        // The preset's own surfaces, fills and borders, measured against #171717 in app.css.
        tokens[ThemeToken.Bg].ShouldBe("#171717");
        tokens[ThemeToken.Surface].ShouldBe("#1c1c1c");
        tokens[ThemeToken.SurfaceRaised].ShouldBe("#242424");
        tokens[ThemeToken.Fill].ShouldBe("#292929");
        tokens[ThemeToken.FillStrong].ShouldBe("#3a3a3a");
        tokens[ThemeToken.Border].ShouldBe("#383838");
        tokens[ThemeToken.BorderStronger].ShouldBe("#5c5c5c");
        tokens[ThemeToken.Inverse].ShouldBe("#f2f2f2");
        tokens[ThemeToken.OnInverse].ShouldBe("#202020");
        tokens[ThemeToken.Text].ShouldBe("#ececec");
        tokens[ThemeToken.TextMuted].ShouldBe("#aaaaaa");
        tokens[ThemeToken.TextFaint].ShouldBe("#858585");
        tokens[ThemeToken.TextDisabled].ShouldBe("#707070");
    }

    [Fact]
    public void ShouldReproduceThePresetContrastsWithinRounding()
    {
        var tokens = ThemePalette.Generate(new CustomThemeSeeds("#171717")).Tokens;

        foreach (var (foreground, background, expected) in new[]
                 {
                     (ThemeToken.Text, ThemeToken.Bg, 15.18),
                     (ThemeToken.TextMuted, ThemeToken.Bg, 7.72),
                     (ThemeToken.TextSubtle, ThemeToken.Bg, 5.99),
                     (ThemeToken.TextFaint, ThemeToken.Bg, 4.86),
                     (ThemeToken.TextDisabled, ThemeToken.Bg, 3.62),
                     (ThemeToken.Surface, ThemeToken.Bg, 1.05),
                     (ThemeToken.SurfaceRaised, ThemeToken.Bg, 1.15),
                     (ThemeToken.Fill, ThemeToken.Bg, 1.23),
                     (ThemeToken.BorderStronger, ThemeToken.Bg, 2.68),
                     (ThemeToken.OnInverse, ThemeToken.Inverse, 14.55)
                 })
        {
            ThemePalette.Contrast(tokens[foreground], tokens[background]).ShouldBe(expected, 0.06,
                $"{foreground} on {background}");
        }
    }

    [Fact]
    public void ShouldKeepEveryTextRoleReadableForAnySeedAndDial()
    {
        string[] seeds =
        [
            "#171717", "#101720", "#212121", "#0a0a0a", "#2b2320", "#20262b", "#241f2b", "#1b2b22",
            "#ffffff", "#fafafa", "#f3f3f3", "#dedede", "#e8e2d6", "#dfe6ee", "#eef3e8", "#fff8ec"
        ];

        foreach (var seed in seeds)
        foreach (var contrast in new[] { 0, 50, 100 })
        foreach (var saturation in new[] { 0, 100 })
        {
            var result = ThemePalette.Generate(new CustomThemeSeeds(seed, Contrast: contrast, Saturation: saturation));
            var tokens = result.Tokens;

            foreach (var (foreground, background) in new[]
                     {
                         (ThemeToken.Text, ThemeToken.Bg),
                         (ThemeToken.TextMuted, ThemeToken.Bg),
                         (ThemeToken.TextFaint, ThemeToken.Bg),
                         (ThemeToken.Text, ThemeToken.Fill)
                     })
                ThemePalette.Contrast(tokens[foreground], tokens[background])
                    .ShouldBeGreaterThanOrEqualTo(ThemePalette.TextTarget,
                        $"{seed} contrast {contrast} saturation {saturation}: {foreground} on {background}");

            ThemePalette.Contrast(tokens[ThemeToken.TextFaint], tokens[ThemeToken.Bg])
                .ShouldBeGreaterThanOrEqualTo(ThemePalette.TextTarget);
            result.MeetsReadableContrast.ShouldBeTrue(
                $"{seed} contrast {contrast} saturation {saturation}: "
                + string.Join(", ", result.Shortfalls.Select(finding => $"{finding.Label} {finding.Ratio} < {finding.Target}")));
            result.Shortfalls.ShouldBeEmpty();
        }
    }

    [Fact]
    public void ShouldReportASeedThatCannotCarryReadableText()
    {
        // A mid tone composite: a filled row sits on the same side as the page's text, so on a background
        // this light the row is darker and the text cannot read on it at 4.5:1 although it can on the
        // page. The engine must report the shortfall instead of shipping an unreadable theme in silence.
        var result = ThemePalette.Generate(new CustomThemeSeeds("#5a7a6a"));

        result.MeetsReadableContrast.ShouldBeFalse();
        result.Shortfalls.ShouldNotBeEmpty();
        result.Shortfalls.ShouldAllBe(finding => finding.Ratio < finding.Target);
    }

    [Fact]
    public void ShouldPutTheRampOnTheSideItsRoleSitsOn()
    {
        var dark = ThemePalette.Generate(new CustomThemeSeeds("#171717")).Tokens;
        var light = ThemePalette.Generate(new CustomThemeSeeds("#ffffff")).Tokens;

        // A dark theme raises surfaces towards white and recesses the page's sunken layer towards black.
        Luminance(dark[ThemeToken.BgSunken]).ShouldBeLessThan(Luminance(dark[ThemeToken.Bg]));
        Luminance(dark[ThemeToken.Surface]).ShouldBeGreaterThan(Luminance(dark[ThemeToken.Bg]));
        Luminance(dark[ThemeToken.SurfaceRaised]).ShouldBeGreaterThan(Luminance(dark[ThemeToken.Surface]));

        // A light theme does the same in the other direction: raised surfaces are darker than the page
        // and the sunken layer is darker still, while text is darker than everything.
        Luminance(light[ThemeToken.SurfaceRaised]).ShouldBeGreaterThan(Luminance(light[ThemeToken.Surface]));
        Luminance(light[ThemeToken.Surface]).ShouldBeLessThan(Luminance(light[ThemeToken.Bg]));
        Luminance(light[ThemeToken.Fill]).ShouldBeLessThan(Luminance(light[ThemeToken.Surface]));
        Luminance(light[ThemeToken.TextFaint]).ShouldBeLessThan(Luminance(light[ThemeToken.Fill]));
        Luminance(light[ThemeToken.Inverse]).ShouldBeLessThan(Luminance(light[ThemeToken.Bg]));
        Luminance(light[ThemeToken.OnInverse]).ShouldBeGreaterThan(Luminance(light[ThemeToken.Inverse]));
    }

    [Fact]
    public void ShouldBeDeterministic()
    {
        var seeds = new CustomThemeSeeds("#2b2320", Contrast: 72, Saturation: 40);

        var first = ThemePalette.Generate(seeds);
        var second = ThemePalette.Generate(seeds);

        second.Tokens.ShouldBe(first.Tokens);
        second.Findings.ShouldBe(first.Findings);
    }

    [Fact]
    public void ShouldCarryTheSeedHueIntoSurfacesButKeepTextNearlyNeutral()
    {
        var warm = ThemePalette.Generate(new CustomThemeSeeds("#2b2320", Saturation: 100)).Tokens;
        var tinted = SrgbColor.Parse(warm[ThemeToken.Surface]).ToOklch();
        var text = SrgbColor.Parse(warm[ThemeToken.Text]).ToOklch();

        tinted.Chroma.ShouldBeGreaterThan(0.005);
        Math.Abs(tinted.Hue - SrgbColor.Parse("#2b2320").ToOklch().Hue).ShouldBeLessThan(12);
        text.Chroma.ShouldBeLessThanOrEqualTo(0.02);
    }

    [Fact]
    public void ShouldKeepANeutralSeedNeutral()
    {
        var tokens = ThemePalette.Generate(new CustomThemeSeeds("#171717", Saturation: 100)).Tokens;

        foreach (var role in new[] { ThemeToken.Surface, ThemeToken.Fill, ThemeToken.Border, ThemeToken.Text })
        {
            var oklch = SrgbColor.Parse(tokens[role]).ToOklch();
            oklch.Chroma.ShouldBeLessThan(0.0008, $"{role} of a grey seed must stay grey");
        }
    }

    [Fact]
    public void ShouldFollowTheSaturationDial()
    {
        var muted = ThemePalette.Generate(new CustomThemeSeeds("#20262b", Saturation: 20)).Tokens;
        var saturated = ThemePalette.Generate(new CustomThemeSeeds("#20262b", Saturation: 100)).Tokens;

        SrgbColor.Parse(saturated[ThemeToken.Surface]).ToOklch().Chroma
            .ShouldBeGreaterThan(SrgbColor.Parse(muted[ThemeToken.Surface]).ToOklch().Chroma);
    }

    [Fact]
    public void ShouldFollowTheContrastDial()
    {
        var soft = ThemePalette.Generate(new CustomThemeSeeds("#171717", Contrast: 0)).Tokens;
        var crisp = ThemePalette.Generate(new CustomThemeSeeds("#171717", Contrast: 100)).Tokens;

        ThemePalette.Contrast(crisp[ThemeToken.Text], crisp[ThemeToken.Bg])
            .ShouldBeGreaterThan(ThemePalette.Contrast(soft[ThemeToken.Text], soft[ThemeToken.Bg]));
        ThemePalette.Contrast(crisp[ThemeToken.TextFaint], crisp[ThemeToken.Bg])
            .ShouldBeGreaterThanOrEqualTo(ThemePalette.TextTarget);
    }

    [Fact]
    public void ShouldPickTheFamilyThatGivesTextMoreRoom()
    {
        ThemePalette.FamilyFor(SrgbColor.Parse("#171717")).ShouldBe(ThemeFamily.Dark);
        ThemePalette.FamilyFor(SrgbColor.Parse("#ffffff")).ShouldBe(ThemeFamily.Light);
        new CustomThemeSeeds("#171717").Normalized().Family.ShouldBe(ThemeFamily.Dark);
        new CustomThemeSeeds("#fdfdfd").Normalized().Family.ShouldBe(ThemeFamily.Light);
        // An explicit choice wins over the derived one.
        new CustomThemeSeeds("#808080", ThemeFamily.Dark).Normalized().Family.ShouldBe(ThemeFamily.Dark);
    }

    [Fact]
    public void ShouldFillEveryRoleAPaletteResultPromises()
    {
        var result = ThemePalette.Generate(new CustomThemeSeeds("#1c2330"));

        result.Tokens.Keys.ShouldBe(ThemeToken.All, ignoreOrder: true);
        result.CustomProperties.Keys.ShouldBe(ThemeToken.All.Select(role => "--color-" + role), ignoreOrder: true);
        result.Findings.ShouldNotBeEmpty();
        result.Findings.ShouldAllBe(finding => result.Tokens.ContainsKey(finding.ForegroundRole)
            && result.Tokens.ContainsKey(finding.BackgroundRole));
        result.Findings.ShouldAllBe(finding => finding.Ratio > 1);
    }

    [Fact]
    public void ShouldRejectAColourItCannotParse()
    {
        Should.Throw<FormatException>(() => new CustomThemeSeeds("not-a-colour").Normalized());
        Should.Throw<FormatException>(() => new CustomThemeSeeds("#12345").Normalized());
        new CustomThemeSeeds("#ABCDEF").Normalized().Background.ShouldBe("#abcdef");
        var clamped = new CustomThemeSeeds("#123456", Contrast: 500, Saturation: -20).Normalized();
        clamped.Contrast.ShouldBe(CustomThemeSeeds.MaxContrast);
        clamped.Saturation.ShouldBe(CustomThemeSeeds.MinSaturation);
        clamped.Family.ShouldBe(ThemePalette.FamilyFor(SrgbColor.Parse("#123456")));
    }

    [Fact]
    public void ShouldMeetTheContrastTheStylesheetPromisesForFaintTextOnBothPresetFamilies()
    {
        // The stylesheet says faint text reads at 4.5:1, and both presets hold it on the page. A theme
        // built from a preset's own background must hold it as well.
        foreach (var (seed, family) in new[]
                 {
                     ("#171717", ThemeFamily.Dark), ("#101720", ThemeFamily.Dark), ("#212121", ThemeFamily.Dark),
                     ("#ffffff", ThemeFamily.Light), ("#f3f3f3", ThemeFamily.Light), ("#dedede", ThemeFamily.Light)
                 })
        {
            var result = ThemePalette.Generate(new CustomThemeSeeds(seed, family));
            result.MeetsReadableContrast.ShouldBeTrue($"{seed} ({family})");
            result.Findings.Single(finding => finding.Label == "Faint text on background").IsMet.ShouldBeTrue();
        }
    }

    [Fact]
    public void ShouldRoundTripThroughOklch()
    {
        foreach (var hex in new[] { "#171717", "#7eb4ed", "#e0665f", "#ffffff", "#000000", "#5dc97c", "#8b5cf6" })
        {
            var color = SrgbColor.Parse(hex);
            SrgbColor.FromOklch(color.ToOklch()).ToHex().ShouldBe(hex);
            color.ToOklch().IsInGamut.ShouldBeTrue();
        }
    }

    [Fact]
    public void ShouldBringAnUnshowableColourIntoTheGamutWithoutMovingItsHue()
    {
        var impossible = new OklchColor(0.7, 0.4, 30);
        impossible.IsInGamut.ShouldBeFalse();

        var fitted = impossible.InGamut();
        fitted.IsInGamut.ShouldBeTrue();
        fitted.Hue.ShouldBe(30);
        fitted.Lightness.ShouldBe(0.7, 0.0001);
        fitted.Chroma.ShouldBeLessThan(0.4);
    }

    [Fact]
    public void ShouldMixTheWayColorMixDoes()
    {
        SrgbColor.Mix(SrgbColor.Parse("#ffffff"), SrgbColor.Parse("#000000"), 0.5).ShouldBe(new SrgbColor(128, 128, 128));
        SrgbColor.Mix(SrgbColor.Parse("#e0665f"), SrgbColor.Parse("#171717"), 0.14).ToHex().ShouldBe(
            SrgbColor.Mix(SrgbColor.Parse("#e0665f"), SrgbColor.Parse("#171717"), 0.14).ToHex());
    }

    private static double Luminance(string hex) => SrgbColor.Parse(hex).RelativeLuminance;
}
