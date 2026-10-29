namespace AI.Contracts.Themes;

/// <summary>
/// What the window reports back to <c>app_theme</c>: what it did, what is painted now and what is saved.
/// The model cannot see the page, so a preview comes back with the colours and the contrast the engine
/// measured; that is what it reasons about before it suggests another step.
/// </summary>
/// <param name="Effect">One sentence on what changed on the page, or why nothing did.</param>
/// <param name="Preset">The preset preference, lowercase; it paints the page while no saved theme is active.</param>
/// <param name="Accent">The accent colour, lowercase.</param>
/// <param name="ActiveThemeId">The saved theme painting the page, or null while a preset does.</param>
/// <param name="Themes">Every saved theme.</param>
/// <param name="Preview">The unsaved theme on the page, or null when the page shows what is saved.</param>
/// <param name="Error">Why the request was refused; nothing was changed then.</param>
public sealed record AppThemeState(string Effect, string Preset, string Accent, string? ActiveThemeId,
    IReadOnlyList<AppThemeSummary> Themes, AppThemePreview? Preview, string? Error = null)
{
    /// <summary>The presets a theme can be activated from, as <see cref="AppThemeRequest.Preset"/> spells them.</summary>
    public static readonly IReadOnlyList<string> Presets = ["system", "light", "dark", "darkblue", "gray", "lightgray"];

    /// <summary>The accents Settings offers, as <see cref="AppThemeRequest.Accent"/> spells them.</summary>
    public static readonly IReadOnlyList<string> Accents = ["blue", "teal", "green", "amber", "orange", "pink", "purple", "indigo"];
}

/// <summary>A saved theme as the model sees it: its name and the seeds it was built from.</summary>
public sealed record AppThemeSummary(string Id, string Name, AppThemeSeeds Seeds, bool Active);

/// <summary>
/// An unsaved theme painted on the page. <see cref="BaseThemeId"/> is the saved theme it was derived
/// from, which is the id to pass to Save to change that theme rather than add another.
/// </summary>
public sealed record AppThemePreview(string? BaseThemeId, string Name, AppThemeSeeds Seeds,
    IReadOnlyDictionary<string, string> Colors, bool MeetsReadableContrast,
    IReadOnlyList<ThemeContrastFinding> Findings);

/// <summary>
/// The seeds in the words the tool takes: the family is spelled <c>dark</c> or <c>light</c> so the model
/// can send back what it read without knowing how an enum is serialised.
/// </summary>
public sealed record AppThemeSeeds(string Background, string Family, int Contrast, int Saturation)
{
    public static AppThemeSeeds From(CustomThemeSeeds seeds) =>
        new(seeds.Background, CustomTheme.FamilyOf(seeds), seeds.Contrast, seeds.Saturation);
}
