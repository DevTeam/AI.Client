namespace AI.Contracts.Themes;

using System.Globalization;

/// <summary>
/// Builds a whole palette from a few seeds. A theme is not a set of offsets in lightness: every role
/// carries the contrast ratio it must reach against the surface it is read on, measured from the
/// presets in app.css, and the engine solves for the colour that reaches it in the seed's hue. That is
/// what makes a theme on a mid grey or a saturated background honest — a ratio this background cannot
/// carry is reported instead of being silently missed.
/// </summary>
/// <remarks>
/// The engine is a pure function of <see cref="CustomThemeSeeds"/>: the same seeds always give the
/// same palette, and nothing here reads a clock, a random source or the page. It lives in the shared
/// contracts because the studio builds a theme before saving it and the model-facing skill builds a
/// candidate from a description; both must use this one implementation.
/// </remarks>
public static class ThemePalette
{
    /// <summary>The ratio every readable text role must reach; the stylesheet promises it for faint text.</summary>
    public const double TextTarget = 4.5;

    /// <summary>A non-text indicator, such as a status dot, needs this much.</summary>
    public const double IndicatorTarget = 3.0;

    // Contrast targets, measured from the dark and the light presets of app.css against the page
    // background, each with the side of the page its role sits on. The two families differ because a
    // dark surface steps up towards white and a light one down towards black by different amounts for
    // the same perceived difference, and because a raised surface leads away from the page in one and
    // back towards it in the other.
    private static readonly (string Role, double Ratio, Side Side)[] DarkTargets =
    [
        (ThemeToken.BgSunken, 1.05, Side.Opposite), (ThemeToken.BgInset, 1.02, Side.Opposite),
        (ThemeToken.InputBg, 1.04, Side.Opposite),
        (ThemeToken.Surface, 1.05, Side.WithText), (ThemeToken.SurfaceRaised, 1.15, Side.WithText),
        (ThemeToken.Fill, 1.23, Side.WithText), (ThemeToken.FillHover, 1.32, Side.WithText),
        (ThemeToken.FillActive, 1.42, Side.WithText), (ThemeToken.FillStrong, 1.58, Side.WithText),
        (ThemeToken.FillStronger, 1.84, Side.WithText),
        (ThemeToken.BorderSubtle, 1.23, Side.WithText), (ThemeToken.Border, 1.53, Side.WithText),
        (ThemeToken.BorderStrong, 1.96, Side.WithText), (ThemeToken.BorderStronger, 2.68, Side.WithText)
    ];

    private static readonly (string Role, double Ratio, Side Side)[] LightTargets =
    [
        (ThemeToken.BgSunken, 1.11, Side.WithText), (ThemeToken.BgInset, 1.08, Side.WithText),
        (ThemeToken.InputBg, 1.01, Side.WithText),
        (ThemeToken.Surface, 1.04, Side.WithText), (ThemeToken.SurfaceRaised, 1.01, Side.Opposite),
        (ThemeToken.Fill, 1.14, Side.WithText), (ThemeToken.FillHover, 1.21, Side.WithText),
        (ThemeToken.FillActive, 1.30, Side.WithText), (ThemeToken.FillStrong, 1.41, Side.WithText),
        (ThemeToken.FillStronger, 1.61, Side.WithText),
        (ThemeToken.BorderSubtle, 1.17, Side.WithText), (ThemeToken.Border, 1.32, Side.WithText),
        (ThemeToken.BorderStrong, 1.56, Side.WithText), (ThemeToken.BorderStronger, 2.10, Side.WithText)
    ];

    // Text levels, as contrasts against the page they are read on, measured in the dark preset.
    // The light preset lands within a tenth of each one, so a theme carries the same ladder either
    // way. The darkest levels are those; a dial raises them, and the floor keeps faint text readable.
    private static readonly (string Role, double Ratio)[] TextLevels =
    [
        (ThemeToken.TextStrong, 17.93), (ThemeToken.Text, 15.18), (ThemeToken.TextSecondary, 12.34),
        (ThemeToken.TextMuted, 7.72), (ThemeToken.TextSubtle, 5.99), (ThemeToken.TextFaint, 4.86),
        (ThemeToken.TextDisabled, 3.62)
    ];

    // Status hues are fixed so a theme never repaints what a status means; only their lightness
    // follows the family and the background.
    private const double DangerHue = 25.5;
    private const double WarningHue = 45.1;
    private const double SuccessHue = 150.7;

    private static readonly (string Role, double Hue, double DarkChroma, double LightChroma)[] StatusHues =
    [
        (ThemeToken.Danger, DangerHue, 0.154, 0.190),
        (ThemeToken.DangerText, DangerHue, 0.100, 0.178),
        (ThemeToken.DangerTextStrong, DangerHue, 0.130, 0.150),
        (ThemeToken.Warning, WarningHue, 0.166, 0.118),
        (ThemeToken.WarningText, WarningHue, 0.150, 0.100),
        (ThemeToken.Success, SuccessHue, 0.149, 0.142),
        (ThemeToken.SuccessText, SuccessHue, 0.082, 0.090)
    ];

    public static ThemePaletteResult Generate(CustomThemeSeeds seeds)
    {
        var normalized = seeds.Normalized();
        var family = normalized.Family ?? ThemeFamily.Dark;
        var background = SrgbColor.Parse(normalized.Background);
        var backgroundOklch = background.ToOklch();
        var backgroundLuminance = background.RelativeLuminance;
        var chroma = ChromaScale(normalized.Saturation, backgroundOklch.Chroma);
        var spread = SpreadScale(normalized.Contrast);

        var tokens = new Dictionary<string, string>(StringComparer.Ordinal) { [ThemeToken.Bg] = background.ToHex() };

        foreach (var (role, ratio, side) in family == ThemeFamily.Dark ? DarkTargets : LightTargets)
        {
            tokens[role] = Solve(backgroundLuminance, ratio, side, family, backgroundOklch.Hue,
                SurfaceChroma(chroma, role));
        }

        // Text is read on the page, on a raised surface and on a filled row. The binding one is
        // whichever sits closest to the text, so text is solved against that: the other two then have
        // more room rather than less.
        // Text is read on the page, which is where the presets promise 4.5:1 for faint text; a raised
        // surface and a filled row then get slightly less, exactly as they do in the presets. The dial
        // raises the levels' distance from each other but can never take a readable role under the
        // floor the stylesheet promises, so a soft theme stays a readable one.
        foreach (var (role, ratio) in TextLevels)
        {
            var target = role == ThemeToken.TextDisabled
                ? ratio
                : Math.Max(ratio * spread, TextTarget);
            tokens[role] = Solve(backgroundLuminance, target, Side.WithText, family,
                backgroundOklch.Hue, Math.Min(chroma, 0.02));
        }

        // The one high-contrast control sits where the text sits, and what is written on it comes from
        // the other side.
        tokens[ThemeToken.Inverse] = Solve(backgroundLuminance, 16.0, Side.WithText, family, backgroundOklch.Hue, chroma * 0.5);
        tokens[ThemeToken.InverseHover] = Solve(backgroundLuminance, 17.9, Side.WithText, family, backgroundOklch.Hue, chroma * 0.5);
        tokens[ThemeToken.InverseActive] = Solve(backgroundLuminance, 13.2, Side.WithText, family, backgroundOklch.Hue, chroma * 0.5);
        var inverseLuminance = SrgbColor.Parse(tokens[ThemeToken.Inverse]).RelativeLuminance;
        tokens[ThemeToken.OnInverse] = Solve(inverseLuminance, 14.5, Side.Opposite, family, backgroundOklch.Hue, chroma * 0.3);

        foreach (var (role, hue, darkChroma, lightChroma) in StatusHues)
        {
            var indicator = role is ThemeToken.Danger or ThemeToken.Warning or ThemeToken.Success;
            var target = indicator
                ? Math.Max(IndicatorTarget * spread, IndicatorTarget * 0.85)
                : Math.Max(5.4 * spread, TextTarget);
            tokens[role] = Solve(backgroundLuminance, target, Side.WithText, family, hue,
                family == ThemeFamily.Dark ? darkChroma : lightChroma);
        }

        // Status tints are mixed from the status colour into the page, but only as far as the word on
        // them stays readable: the presets mix 14% and the word reaches 7.6:1, while a soft theme has a
        // flatter status colour and cannot take that much without taking the word down with it.
        foreach (var (role, weight) in TintWeights(family))
        {
            var isDanger = role.StartsWith("danger", StringComparison.Ordinal);
            tokens[role] = TintWithinContrast(isDanger ? tokens[ThemeToken.Danger] : tokens[ThemeToken.Success],
                background, weight, isDanger ? tokens[ThemeToken.DangerText] : tokens[ThemeToken.SuccessText],
                TextTarget);
        }

        var findings = Measure(tokens);
        return new ThemePaletteResult(normalized, tokens, findings, findings.All(finding => finding.IsMet));
    }

    /// <summary>The WCAG contrast ratio of two <c>#rrggbb</c> colours, rounded to two decimals.</summary>
    public static double Contrast(string first, string second) =>
        Math.Round(SrgbColor.ContrastRatio(SrgbColor.Parse(first), SrgbColor.Parse(second)), 2);

    /// <summary>
    /// The pairs the studio shows. Each one is a pair the stylesheet relies on and the presets hold:
    /// text on the page, body text on a filled row, what is written on the send control, status words
    /// on the page and on their tint. Faint text on a raised surface is deliberately not one of them —
    /// the dark preset reads 4.21 there — so a generated theme is not asked for more than a preset.
    /// </summary>
    public static IReadOnlyList<ThemeContrastFinding> Measure(IReadOnlyDictionary<string, string> tokens)
    {
        var pairs = new (string Label, string Foreground, string Background, double Target)[]
        {
            ("Body text on background", ThemeToken.Text, ThemeToken.Bg, TextTarget),
            ("Muted text on background", ThemeToken.TextMuted, ThemeToken.Bg, TextTarget),
            ("Faint text on background", ThemeToken.TextFaint, ThemeToken.Bg, TextTarget),
            ("Body text on a filled row", ThemeToken.Text, ThemeToken.Fill, TextTarget),
            ("Text on the send control", ThemeToken.OnInverse, ThemeToken.Inverse, TextTarget),
            ("Danger word on background", ThemeToken.DangerText, ThemeToken.Bg, TextTarget),
            ("Warning word on background", ThemeToken.WarningText, ThemeToken.Bg, TextTarget),
            ("Success word on background", ThemeToken.SuccessText, ThemeToken.Bg, TextTarget),
            ("Danger word on its tint", ThemeToken.DangerText, ThemeToken.DangerBg, TextTarget)
        };
        return
        [
            .. pairs.Where(pair => tokens.ContainsKey(pair.Foreground) && tokens.ContainsKey(pair.Background))
                .Select(pair => new ThemeContrastFinding(pair.Label, pair.Foreground, pair.Background,
                    Contrast(tokens[pair.Foreground], tokens[pair.Background]), pair.Target))
        ];
    }

    /// <summary>
    /// The highest ratio this background can reach towards its far pole: white for a dark theme, black
    /// for a light one. A mid grey background cannot carry white text at 15:1, and saying so is more
    /// useful than pretending the ramp reached it.
    /// </summary>
    public static double Ceiling(string background, ThemeFamily family)
    {
        var luminance = SrgbColor.Parse(background).RelativeLuminance;
        var far = family == ThemeFamily.Dark ? 1.0 : 0.0;
        return Math.Round((Math.Max(luminance, far) + 0.05) / (Math.Min(luminance, far) + 0.05), 2);
    }

    /// <summary>
    /// Which family a background carries best: the one whose far pole gives text more room. A colour
    /// that is neither clearly dark nor clearly light still has one, and it is the one that keeps the
    /// interface readable.
    /// </summary>
    public static ThemeFamily FamilyFor(SrgbColor background)
    {
        var luminance = background.RelativeLuminance;
        var dark = Math.Round((1.0 + 0.05) / (luminance + 0.05), 2);
        var light = Math.Round((luminance + 0.05) / 0.05, 2);
        return dark >= light ? ThemeFamily.Dark : ThemeFamily.Light;
    }
    /// <summary>How much of the seed's chroma a role keeps; text stays nearly neutral, surfaces keep it all.</summary>
    private static double SurfaceChroma(double chroma, string role)
    {
        if (role.StartsWith("text", StringComparison.Ordinal)) return Math.Min(chroma, 0.02);
        if (role.StartsWith("border", StringComparison.Ordinal)) return Math.Min(chroma, 0.06);
        return chroma;
    }

    /// <summary>How much of the seed's chroma reaches the palette; the dial is 1 at its middle.</summary>
    private static double ChromaScale(int saturation, double seedChroma)
    {
        var scaled = seedChroma * (saturation / 50.0);
        // A seed that is grey to the eye must stay grey: a chroma below this rounds to the same
        // channel values anyway, and carrying it would tint a neutral theme.
        return scaled < 0.0008 ? 0 : Math.Clamp(scaled, 0, 0.14);
    }

    /// <summary>How far the contrast dial stretches the gaps above the readable floor.</summary>
    private static double SpreadScale(int contrast) => 1 + ((contrast - 50) / 50.0) * 0.35;

    private static (string Role, double Weight)[] TintWeights(ThemeFamily family) => family == ThemeFamily.Dark
        ? [("danger-bg", 0.14), ("danger-bg-hover", 0.22), ("danger-bg-active", 0.30),
           ("danger-border", 0.38), ("danger-border-strong", 0.52), ("success-border", 0.40)]
        : [("danger-bg", 0.07), ("danger-bg-hover", 0.11), ("danger-bg-active", 0.16),
           ("danger-border", 0.34), ("danger-border-strong", 0.50), ("success-border", 0.40)];

    /// <summary>
    /// The colour that sits at <paramref name="ratio"/> against a surface of
    /// <paramref name="surfaceLuminance"/>, on the requested side, in the given hue. The luminance the
    /// ratio asks for is solved directly and then matched by an Oklch lightness, so the measured ratio
    /// is the ratio that was asked for; the last pass keeps the nearest eight-bit colour, because a
    /// token is written as <c>#rrggbb</c>.
    /// </summary>
    private static string Solve(double surfaceLuminance, double ratio, Side side, ThemeFamily family,
        double hue, double chroma)
    {
        // The text side is lighter on a dark page and darker on a light one; the other side is the
        // opposite. The family decides, not the surface's own brightness, so a mid grey still gets one
        // coherent answer instead of surfaces that disagree about which way is up.
        var lighter = side == Side.WithText ? family == ThemeFamily.Dark : family == ThemeFamily.Light;

        var wanted = lighter
            ? ratio * (surfaceLuminance + 0.05) - 0.05
            : (surfaceLuminance + 0.05) / ratio - 0.05;
        var targetLuminance = Math.Clamp(wanted, 0, 1);

        // An Oklch lightness that renders to this luminance in this hue, by bisection: luminance grows
        // with lightness, so the search is monotone.
        var low = 0.0;
        var high = 1.0;
        for (var step = 0; step < 40; step++)
        {
            var middle = (low + high) / 2;
            if (Render(middle, hue, chroma).RelativeLuminance < targetLuminance) low = middle;
            else high = middle;
        }

        var candidate = (low + high) / 2;

        // An eight-bit colour rarely lands on the exact luminance, so the promise is kept on the safe
        // side of the target: among the neighbouring colours that still reach the ratio, the one closest
        // to it wins. A ratio that measures 4.48 where 4.5 was promised is a broken promise to a person
        // who asked for a readable theme, and overshooting by a step is invisible. Contrast grows as a
        // colour moves away from its surface in either direction, so reaching the ratio is the same test
        // on both sides.
        const double guard = 0.005;
        var best = Render(candidate, hue, chroma);
        var bestError = double.MaxValue;
        var fallback = best;
        var fallbackError = double.MaxValue;
        for (var step = -12; step <= 12; step++)
        {
            var next = Render(Math.Clamp(candidate + step * 0.002, 0, 1), hue, chroma);
            var error = Math.Abs(next.RelativeLuminance - targetLuminance);
            if (error < fallbackError)
            {
                fallback = next;
                fallbackError = error;
            }
            if (ExactContrast(next, surfaceLuminance) < ratio - guard || error >= bestError) continue;
            best = next;
            bestError = error;
        }
        return (bestError == double.MaxValue ? fallback : best).ToHex();
    }

    private static double ExactContrast(SrgbColor color, double surfaceLuminance)
    {
        var luminance = color.RelativeLuminance;
        return (Math.Max(luminance, surfaceLuminance) + 0.05) / (Math.Min(luminance, surfaceLuminance) + 0.05);
    }

    /// <summary>
    /// A tint of <paramref name="source"/> into <paramref name="background"/>, weakened until the word on
    /// it keeps its contrast. A tinted background that carries a word is a surface for that word, so the
    /// strongest usable tint is the strongest one the word survives, up to the share the presets use.
    /// </summary>
    private static string TintWithinContrast(string source, SrgbColor background, double maxWeight,
        string foreground, double target)
    {
        var text = SrgbColor.Parse(foreground);
        var baseColor = SrgbColor.Parse(source);
        var best = background;
        for (var step = 20; step >= 0; step--)
        {
            var candidate = SrgbColor.Mix(baseColor, background, maxWeight * step / 20);
            if (SrgbColor.ContrastRatio(text, candidate) < target) continue;
            best = candidate;
            break;
        }
        return best.ToHex();
    }

    private static SrgbColor Render(double lightness, double hue, double chroma) =>
        SrgbColor.FromOklch(new OklchColor(lightness, chroma, hue).InGamut());

    /// <summary>Formats a ratio for the studio and for a model answer: two decimals, invariant culture.</summary>
    public static string FormatRatio(double ratio) => ratio.ToString("0.0#", CultureInfo.InvariantCulture);

    private enum Side
    {
        /// <summary>The side the text is read from: white on a dark page, black on a light one.</summary>
        WithText,

        /// <summary>The other side, where a recessed surface sits.</summary>
        Opposite
    }
}
