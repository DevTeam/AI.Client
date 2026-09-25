namespace AI.Client.Web.Settings;

public interface IThemeService
{
    ValueTask<ThemePreference> GetAsync();

    /// <summary>Saves the preference and repaints the page with it right away.</summary>
    ValueTask SetAsync(ThemePreference preference);
}
