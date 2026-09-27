namespace AI.Web.Settings;

using Microsoft.JSInterop;

/// <summary>
/// Keeps the saved preference and the page in step. The page side lives in js/theme.js, which
/// also applies the saved preference on load and follows the OS while the preference is System.
/// </summary>
public sealed class ThemeService(IClientSettingsService settings, IJSRuntime jsRuntime) : IThemeService
{
    public async ValueTask<ThemePreference> GetAsync() => (await settings.GetAsync()).Theme;

    public async ValueTask SetAsync(ThemePreference preference)
    {
        await settings.UpdateAsync(current => current with { Theme = preference });
        await jsRuntime.InvokeVoidAsync("aiClientTheme.apply", preference.ToString().ToLowerInvariant());
    }
}
