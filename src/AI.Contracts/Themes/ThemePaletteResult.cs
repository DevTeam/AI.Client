namespace AI.Contracts.Themes;

/// <summary>
/// What <see cref="ThemePalette"/> makes of a seed set: the colours, the ratios it measured, and
/// whether every pair the stylesheet promises reached its target. A palette is applied as it is, so
/// the result is stored with the theme: the page colours itself from these values before any script
/// could recompute them, and the same seeds cannot paint differently after an update.
/// </summary>
/// <param name="Seeds">The seeds after normalisation, which is what the studio shows back.</param>
/// <param name="Tokens">Every role of <see cref="ThemeToken.All"/>, under its token name without the <c>--color-</c> prefix.</param>
/// <param name="Findings">The measured pairs, including the ones that missed their target.</param>
/// <param name="MeetsReadableContrast">
/// Every pair reached its target. A palette that does not is still usable and still applied; the
/// studio has to say what is short rather than refuse the person's choice.
/// </param>
public sealed record ThemePaletteResult(CustomThemeSeeds Seeds, IReadOnlyDictionary<string, string> Tokens,
    IReadOnlyList<ThemeContrastFinding> Findings, bool MeetsReadableContrast)
{
    /// <summary>The tokens as CSS custom properties, which is what the page receives.</summary>
    public IReadOnlyDictionary<string, string> CustomProperties =>
        Tokens.ToDictionary(pair => "--color-" + pair.Key, pair => pair.Value, StringComparer.Ordinal);

    public IReadOnlyList<ThemeContrastFinding> Shortfalls =>
        [.. Findings.Where(finding => !finding.IsMet)];
}
