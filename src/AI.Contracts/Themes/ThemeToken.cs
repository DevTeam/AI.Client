namespace AI.Contracts.Themes;

/// <summary>
/// The colour roles a theme is made of, in the order the ramp builds them. A user theme names a few
/// seeds and every role below is derived from them; the list is also what makes an unknown or missing
/// role detectable, because a token outside it can never be generated.
/// </summary>
/// <remarks>
/// The names are the <c>--color-*</c> tokens of app.css without the prefix, so a generated palette is
/// applied under exactly the names the stylesheet already uses. Roles that carry a meaning rather than
/// decoration — the application brand, the unread mark, the "a run is in progress" grey, the accent the
/// person picked — are deliberately absent and listed in <see cref="Reserved"/> instead: a theme must
/// not repaint them.
/// </remarks>
public static class ThemeToken
{
    // Surfaces, from the deepest to the most raised.
    public const string BgSunken = "bg-sunken";
    public const string BgInset = "bg-inset";
    public const string Bg = "bg";
    public const string Surface = "surface";
    public const string SurfaceRaised = "surface-raised";
    public const string InputBg = "input-bg";

    // Fills: rows, chips and buttons at rest, hovered and pressed.
    public const string Fill = "fill";
    public const string FillHover = "fill-hover";
    public const string FillActive = "fill-active";
    public const string FillStrong = "fill-strong";
    public const string FillStronger = "fill-stronger";

    // The one high-contrast control (Send) and what sits on it.
    public const string Inverse = "inverse";
    public const string InverseHover = "inverse-hover";
    public const string InverseActive = "inverse-active";
    public const string OnInverse = "on-inverse";

    // Text, by importance.
    public const string TextStrong = "text-strong";
    public const string Text = "text";
    public const string TextSecondary = "text-secondary";
    public const string TextMuted = "text-muted";
    public const string TextSubtle = "text-subtle";
    public const string TextFaint = "text-faint";
    public const string TextDisabled = "text-disabled";

    // Borders.
    public const string BorderSubtle = "border-subtle";
    public const string Border = "border";
    public const string BorderStrong = "border-strong";
    public const string BorderStronger = "border-stronger";

    // Status colours. The base one is for icons and dots, -text for words on a surface.
    public const string Danger = "danger";
    public const string DangerText = "danger-text";
    public const string DangerTextStrong = "danger-text-strong";
    public const string DangerBg = "danger-bg";
    public const string DangerBgHover = "danger-bg-hover";
    public const string DangerBgActive = "danger-bg-active";
    public const string DangerBorder = "danger-border";
    public const string DangerBorderStrong = "danger-border-strong";
    public const string Warning = "warning";
    public const string WarningText = "warning-text";
    public const string Success = "success";
    public const string SuccessText = "success-text";
    public const string SuccessBorder = "success-border";

    /// <summary>Every role a theme controls, in ramp order: surfaces, fills, inverse, text, borders, status.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        BgSunken, BgInset, Bg, Surface, SurfaceRaised, InputBg,
        Fill, FillHover, FillActive, FillStrong, FillStronger,
        Inverse, InverseHover, InverseActive, OnInverse,
        TextStrong, Text, TextSecondary, TextMuted, TextSubtle, TextFaint, TextDisabled,
        BorderSubtle, Border, BorderStrong, BorderStronger,
        Danger, DangerText, DangerTextStrong, DangerBg, DangerBgHover, DangerBgActive, DangerBorder,
        DangerBorderStrong, Warning, WarningText, Success, SuccessText, SuccessBorder
    ];

    /// <summary>
    /// Roles a theme may not change, each with the reason it stays the application's. The page keeps
    /// them from its own preset palette under a user theme, so a colour that says what something means
    /// never follows a person's taste.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Reserved { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["unread"] = "an unseen result keeps the blue accent's -strong under every accent, so its meaning never changes with the colours",
        ["brand"] = "the application's own blue on the startup screen, which shows the product rather than the choice",
        ["generating"] = "a run in progress is a neutral grey in every theme; the mark's shape, not the colour, sets it apart",
        ["user-bubble"] = "derived from the accent the person picked, which a theme may borrow but not replace",
        ["user-bubble-border"] = "derived from the accent the person picked",
        ["user-bubble-inset"] = "derived from the accent the person picked",
        ["diff-added-bg"] = "a diff's meaning is green added and red removed, whatever the theme",
        ["diff-removed-bg"] = "a diff's meaning is green added and red removed, whatever the theme",
        ["highlight-bg"] = "a search hit is painted from the accent, not from the theme",
        ["highlight-fg"] = "a search hit is painted from the accent, not from the theme",
        ["accent"] = "chosen separately in Settings → Accent colour, and applied to the same tokens under every theme",
        ["accent-strong"] = "derived from the accent swatch",
        ["accent-text"] = "derived from the accent swatch",
        ["accent-bg"] = "derived from the accent swatch and the theme's own background",
        ["accent-bg-hover"] = "derived from the accent swatch and the theme's own background",
        ["accent-bg-active"] = "derived from the accent swatch and the theme's own background",
        ["accent-fill"] = "derived from the accent swatch",
        ["on-accent"] = "what is written on the solid accent block, measured against the accent, not the theme",
        ["overlay-faint"] = "a translucent white-or-black film that has to work on every surface of the theme, not only the page",
        ["overlay-hover"] = "a translucent film rather than a colour, so it reads on rows, chips and surfaces alike",
        ["overlay-active"] = "a translucent film rather than a colour, so it reads on rows, chips and surfaces alike",
        ["overlay-sunken"] = "a translucent film that deepens whatever sits under it",
        ["border-translucent"] = "a translucent hairline that has to read on every surface of the theme"
    };

    /// <summary>
    /// The context-window layers, each of which keeps a fixed hue so neighbouring arcs stay apart. Kept
    /// apart from <see cref="Reserved"/> only because a dictionary initialiser cannot give one reason to
    /// several keys; the reason is the same.
    /// </summary>
    public static IReadOnlyList<string> ReservedContextLayers { get; } =
        ["context-instructions", "context-tools", "context-history", "context-draft", "context-reserve", "context-overhead"];

    /// <summary>Whether a role may be set by a user theme.</summary>
    public static bool IsThemable(string role) => All.Contains(role);

    /// <summary>Whether a role is one a theme must keep: it says what a mark means, not how it looks.</summary>
    public static bool IsReserved(string role) => Reserved.ContainsKey(role) || ReservedContextLayers.Contains(role);
}
