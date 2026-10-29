namespace AI.Web.Settings;

using AI.Contracts.Themes;
using Microsoft.JSInterop;

/// <summary>
/// Keeps the saved preference and the page in step. The page side lives in js/theme.js, which
/// also applies the saved preference on load and follows the OS while the preference is System.
/// </summary>
/// <remarks>
/// A saved theme carries the palette the engine produced, and the page applies those values itself.
/// Every method here therefore only decides which theme is active and hands it to the script; it never
/// computes a colour, so the studio and the skill that builds a theme from a description cannot paint
/// the page differently from each other.
///
/// The accent belongs to what paints the page: a saved theme keeps its own, and the presets share the
/// one in <see cref="ClientSettings.Accent"/>. Every path that changes what is painted therefore ends by
/// telling the script which accent goes with it, so switching themes never leaves the previous one's.
/// </remarks>
public sealed class ThemeService(IClientSettingsService settings, IJSRuntime jsRuntime) : IThemeService
{
    public async ValueTask<ThemePreference> GetAsync() => (await settings.GetAsync()).Theme;

    public async ValueTask SetAsync(ThemePreference preference)
    {
        // Choosing a preset also drops the active saved theme. Without that, the theme's inline palette
        // would stay on <html> and the preset would be painted underneath someone else's colours.
        var current = await settings.UpdateAsync(current => current with { Theme = preference, ActiveCustomThemeId = null });
        await jsRuntime.InvokeVoidAsync("aiClientTheme.applyCustomTheme", null);
        await jsRuntime.InvokeVoidAsync("aiClientTheme.apply", preference.ToString().ToLowerInvariant());
        await ApplyAccentAsync(current.Accent);
    }

    /// <summary>The accent on the page: the active saved theme's own, or the presets' one.</summary>
    public async ValueTask<AccentColor> GetAccentAsync()
    {
        var current = await settings.GetAsync();
        return AccentOf(Find(current, current.ActiveCustomThemeId), current);
    }

    /// <summary>
    /// Changes the accent of what paints the page: the active saved theme keeps it as its own, and while
    /// a preset is in use it becomes the presets' accent. A theme's accent never leaks into the presets.
    /// </summary>
    public async ValueTask SetAccentAsync(AccentColor accent)
    {
        var name = Name(accent);
        await settings.UpdateAsync(current => Find(current, current.ActiveCustomThemeId) is { } active
            ? current with
            {
                CustomThemes = [.. current.CustomThemes.Select(theme => theme.Id == active.Id ? theme with { Accent = name } : theme)]
            }
            : current with { Accent = accent });
        await ApplyAccentAsync(accent);
    }

    public async ValueTask<int> GetCornerRoundnessAsync() => (await settings.GetAsync()).CornerRoundnessPercent;

    public async ValueTask SetCornerRoundnessAsync(int percent)
    {
        percent = Math.Clamp(percent, 0, 200);
        await settings.UpdateAsync(current => current with { CornerRoundnessPercent = percent });
        await jsRuntime.InvokeVoidAsync("aiClientTheme.applyCornerRoundness", percent);
    }

    public async ValueTask<IReadOnlyList<CustomTheme>> GetCustomThemesAsync() => (await settings.GetAsync()).CustomThemes;

    public async ValueTask<CustomTheme?> GetActiveCustomThemeAsync()
    {
        var current = await settings.GetAsync();
        return Find(current, current.ActiveCustomThemeId);
    }

    /// <summary>
    /// Stores the seed set and the palette it produced. A saved id keeps its place in the list; an empty
    /// or unknown one appends a new theme. The palette is rewritten even when the name stands still,
    /// because two saved themes share the page's <c>data-theme="custom"</c> and only colours tell them
    /// apart, so an edit that did not repaint would look like nothing happened. The theme keeps the
    /// accent it had, and a new one takes the accent on the page: what was seen is what is saved.
    /// </summary>
    public async ValueTask<CustomTheme> SaveCustomThemeAsync(string? id, string name, CustomThemeSeeds seeds)
    {
        var before = await settings.GetAsync();
        var created = CustomTheme.Create(NewId(id), name, seeds);
        var saved = created with
        {
            Accent = Find(before, created.Id)?.Accent ?? Name(AccentOf(Find(before, before.ActiveCustomThemeId), before))
        };
        await settings.UpdateAsync(current =>
        {
            var existing = current.CustomThemes.ToList();
            var index = existing.FindIndex(theme => theme.Id == saved.Id);
            if (index >= 0) existing[index] = saved;
            else existing.Add(saved);
            return current with { CustomThemes = existing, ActiveCustomThemeId = saved.Id };
        });
        await jsRuntime.InvokeVoidAsync("aiClientTheme.applyCustomTheme", saved);
        await ApplyAccentAsync(AccentOf(saved, before));
        return saved;
    }

    public async ValueTask SetActiveCustomThemeAsync(string? id)
    {
        var current = await settings.GetAsync();
        // An id that no longer names a saved theme shows the preset rather than an empty page.
        var theme = Find(current, id);
        await settings.UpdateAsync(settings => settings with { ActiveCustomThemeId = theme?.Id });
        await jsRuntime.InvokeVoidAsync("aiClientTheme.applyCustomTheme", theme);
        await ApplyAccentAsync(AccentOf(theme, current));
    }

    /// <summary>
    /// The accent stays the one on the page unless the theme brings its own: a candidate built from a
    /// saved theme shows that theme's stored accent (the studio's copy of the theme may predate an accent
    /// change), and a new one keeps whatever the person is looking at.
    /// </summary>
    public async ValueTask PreviewCustomThemeAsync(CustomTheme? theme)
    {
        var current = await settings.GetAsync();
        var shown = theme is null ? null
            : Find(current, theme.Id) is { Accent: not null } saved ? saved
            : theme.Accent is not null ? theme
            : Find(current, current.ActiveCustomThemeId);
        await jsRuntime.InvokeVoidAsync("aiClientTheme.applyCustomTheme", theme);
        await ApplyAccentAsync(AccentOf(shown, current));
    }

    public async ValueTask<AccentColor> GetPresetAccentAsync() => (await settings.GetAsync()).Accent;

    public async ValueTask DeleteCustomThemeAsync(string id)
    {
        var current = await settings.GetAsync();
        var remaining = current.CustomThemes.Where(theme => theme.Id != id).ToList();
        if (remaining.Count == current.CustomThemes.Count) return;
        var wasActive = current.ActiveCustomThemeId == id;
        await settings.UpdateAsync(settings => settings with
        {
            CustomThemes = remaining,
            ActiveCustomThemeId = wasActive ? null : settings.ActiveCustomThemeId
        });
        // A removed theme must leave the page: its palette is inline, so it would survive in memory.
        if (!wasActive) return;
        await jsRuntime.InvokeVoidAsync("aiClientTheme.applyCustomTheme", null);
        await ApplyAccentAsync(current.Accent);
    }

    private ValueTask ApplyAccentAsync(AccentColor accent) => jsRuntime.InvokeVoidAsync("aiClientTheme.applyAccent", Name(accent));

    private static CustomTheme? Find(ClientSettings current, string? id) =>
        id is null ? null : current.CustomThemes.FirstOrDefault(theme => theme.Id == id);

    /// <summary>A theme's own accent, or the presets' one for a preset or a theme saved without one.</summary>
    private static AccentColor AccentOf(CustomTheme? theme, ClientSettings current) =>
        theme?.Accent is { } name && Enum.TryParse<AccentColor>(name, true, out var accent) && Enum.IsDefined(accent)
            ? accent
            : current.Accent;

    private static string Name(AccentColor accent) => accent.ToString().ToLowerInvariant();

    /// <summary>
    /// The id a saved theme gets. The studio's preview id is not a theme: saving what it shows must
    /// create a new entry rather than overwrite whichever theme happened to be painted.
    /// </summary>
    private static string NewId(string? id) =>
        string.IsNullOrWhiteSpace(id) || id == CustomTheme.PreviewId ? Guid.NewGuid().ToString("n") : id;
}
