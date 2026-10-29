namespace AI.Contracts.Themes;

/// <summary>
/// What a chat asks the person's window to do with its themes. Themes are kept by the client, not by
/// the Host, so the <c>app_theme</c> tool hands this to an open window on the navigation channel and the
/// window answers with an <see cref="AppThemeState"/>.
/// </summary>
/// <param name="Operation">One of <see cref="Operations"/>.</param>
/// <param name="ThemeId">
/// Preview: the saved theme whose seeds the omitted ones are taken from. Save: the saved theme to
/// overwrite; omitted, the theme is added as a new one. Activate: the saved theme to paint.
/// </param>
/// <param name="Preset">Activate: a preset to paint instead of a saved theme (<c>system</c>, <c>dark</c>, …).</param>
/// <param name="Name">Preview and Save: what the theme is called.</param>
/// <param name="Background">Preview and Save: the page background, <c>#rrggbb</c>.</param>
/// <param name="Family">Preview and Save: <c>dark</c> or <c>light</c>; omitted, the background decides.</param>
/// <param name="Contrast">Preview and Save: 0 to 100.</param>
/// <param name="Saturation">Preview and Save: 0 to 100.</param>
/// <param name="Accent">Save and Activate: the accent colour to use with it, one of the Settings swatches.</param>
public sealed record AppThemeRequest(string Operation, string? ThemeId = null, string? Preset = null,
    string? Name = null, string? Background = null, string? Family = null, int? Contrast = null,
    int? Saturation = null, string? Accent = null)
{
    public const string Get = "get";
    public const string Preview = "preview";
    public const string Save = "save";
    public const string Activate = "activate";
    public const string Discard = "discard";

    public static readonly IReadOnlyList<string> Operations = [Get, Preview, Save, Activate, Discard];
}
