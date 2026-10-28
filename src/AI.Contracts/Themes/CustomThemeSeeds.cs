namespace AI.Contracts.Themes;

/// <summary>
/// What a person picks in the theme studio: one background colour and three dials. Everything the
/// page paints is derived from these, so a theme is a few numbers the model can also produce from a
/// description, and the same seeds always give the same palette.
/// </summary>
/// <param name="Background">The page background, <c>#rrggbb</c>: the colour of the largest surface.</param>
/// <param name="Family">
/// Which way the ramp runs away from the background. Null reads it from the background, taking the
/// family that gives text more room; the studio shows the answer and lets it be overridden, so a dark
/// theme on a mid grey can still be chosen on purpose.
/// </param>
/// <param name="Contrast">
/// How far text sits from its surface, 0 to 100. It never takes faint text below the 4.5:1 the
/// stylesheet promises; the upper end simply spreads the levels further apart.
/// </param>
/// <param name="Saturation">
/// How much of the background's own hue reaches surfaces, fills, borders and text, 0 to 100. At 50
/// they keep the seed's chroma; 0 gives a neutral grey theme.
/// </param>
public sealed record CustomThemeSeeds(string Background, ThemeFamily? Family = null, int Contrast = 50,
    int Saturation = 50)
{
    /// <summary>Text roles must stay readable; the stylesheet promises this ratio for faint text.</summary>
    public const double MinimumTextContrast = 4.5;

    public const int MinContrast = 0;
    public const int MaxContrast = 100;
    public const int MinSaturation = 0;
    public const int MaxSaturation = 100;

    /// <summary>Normalised seeds: a stored theme may have been hand-edited or written by another build.</summary>
    public CustomThemeSeeds Normalized()
    {
        var background = SrgbColor.TryParse(Background, out var parsed)
            ? parsed
            : throw new FormatException($"'{Background}' is not a #rrggbb colour.");
        var family = Family ?? ThemePalette.FamilyFor(background);
        return this with
        {
            Background = background.ToHex(),
            Family = family,
            Contrast = Math.Clamp(Contrast, MinContrast, MaxContrast),
            Saturation = Math.Clamp(Saturation, MinSaturation, MaxSaturation)
        };
    }
}
