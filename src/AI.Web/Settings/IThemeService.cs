namespace AI.Web.Settings;

public interface IThemeService
{
    ValueTask<ThemePreference> GetAsync();

    /// <summary>Saves the preference and repaints the page with it right away.</summary>
    ValueTask SetAsync(ThemePreference preference);

    ValueTask<AccentColor> GetAccentAsync();

    /// <summary>Saves the accent and recolours the page with it right away.</summary>
    ValueTask SetAccentAsync(AccentColor accent);
}
