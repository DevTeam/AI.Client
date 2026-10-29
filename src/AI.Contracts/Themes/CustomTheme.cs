namespace AI.Contracts.Themes;

/// <summary>
/// A theme a person built, as it is stored: the seeds they chose and the palette those seeds
/// produced. The palette is kept beside the seeds rather than recomputed on load, because the page
/// paints itself from it before the stylesheet is read, and because an update to the engine must not
/// repaint a saved theme differently from the day it was saved.
/// </summary>
/// <remarks>
/// <see cref="Tokens"/> is keyed by the CSS property the page sets, <c>--color-bg</c> and the like, and
/// <see cref="Family"/> is spelled the way the page's <c>data-theme-family</c> attribute is, so
/// <c>js/theme.js</c> can apply a stored theme as it stands without translating anything and before any
/// .NET code has run. Only roles of <see cref="ThemeToken.All"/> are ever put there; the roles a theme
/// keeps are refused by the engine and by the script alike.
/// </remarks>
public sealed record CustomTheme
{
    /// <summary>The prefix every role of a stored theme carries, which is what the page writes.</summary>
    public const string CssPrefix = "--color-";

    /// <summary>The family spellings the page reads from <c>data-theme-family</c>.</summary>
    public const string DarkFamily = "dark";
    public const string LightFamily = "light";

    /// <summary>How long a name may be; a theme is listed as a pill in Settings.</summary>
    public const int MaxNameLength = 40;

    /// <summary>What a nameless theme is called instead of an empty pill.</summary>
    public const string DefaultName = "My theme";

    /// <summary>The theme the studio starts from: a neutral dark page, no dial turned.</summary>
    public const string DefaultBackground = "#202020";

    /// <summary>
    /// The id of a theme the studio is still choosing. It is painted on the page so the person sees
    /// the colours before saving them, and it is never stored: a saved theme always gets its own id.
    /// </summary>
    public const string PreviewId = "preview";

    public string Id { get; init; } = string.Empty;

    /// <summary>What the person calls it; two themes may share a name, an id never repeats.</summary>
    public string Name { get; init; } = DefaultName;

    /// <summary>The seeds after normalisation, which is what the studio shows back.</summary>
    public CustomThemeSeeds Seeds { get; init; } = new(DefaultBackground);

    /// <summary>
    /// Which way the palette ramps away from its background, as <c>dark</c> or <c>light</c>. The page
    /// needs it as one flat word before it reads the stylesheet, and its light-family rules key on the
    /// attribute, so the stored theme carries it beside the seeds rather than leaving it implied.
    /// </summary>
    public string Family { get; init; } = DarkFamily;

    /// <summary>The resolved palette, under the property names the page uses.</summary>
    public IReadOnlyDictionary<string, string> Tokens { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The accent the theme is shown with, lowercase as the page's <c>data-accent</c> spells it, or null
    /// for one saved before themes kept their own: that theme follows the accent chosen for the presets.
    /// The accent is not part of the palette (a theme may not repaint it), but a theme built around a
    /// colour looks wrong with another, so switching to the theme brings its accent back.
    /// </summary>
    public string? Accent { get; init; }

    /// <summary>
    /// Builds a theme by running <paramref name="seeds"/> through <see cref="ThemePalette"/>. Every
    /// stored theme is made here, so a stored palette is always one the engine produced and never a set
    /// of colours written by hand.
    /// </summary>
    public static CustomTheme Create(string id, string name, CustomThemeSeeds seeds)
    {
        var palette = ThemePalette.Generate(seeds);
        return new CustomTheme
        {
            Id = id,
            Name = CleanName(name),
            Seeds = palette.Seeds,
            Family = FamilyOf(palette.Seeds),
            Tokens = palette.CustomProperties
        };
    }

    /// <summary>The page's word for the family the seeds resolved to.</summary>
    public static string FamilyOf(CustomThemeSeeds seeds) =>
        (seeds.Family ?? ThemePalette.FamilyFor(SrgbColor.Parse(seeds.Background))) == ThemeFamily.Light
            ? LightFamily
            : DarkFamily;

    /// <summary>A name as it is stored: trimmed, single-spaced, capped, and never empty.</summary>
    public static string CleanName(string? name)
    {
        var trimmed = string.Join(' ',
            (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (trimmed.Length == 0) return DefaultName;
        return trimmed.Length <= MaxNameLength ? trimmed : trimmed[..MaxNameLength].TrimEnd();
    }
}
