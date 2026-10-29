namespace AI.Web.Settings;

using AI.Contracts.Themes;

/// <summary>
/// Carries out what a chat asks of the client's themes through <c>app_theme</c>. The window is where
/// themes live, so the Host's tool only forwards the request; this side previews, saves and switches
/// through <see cref="IThemeService"/>, exactly as the studio in Settings does.
/// </summary>
public interface IThemeChatRequests
{
    /// <summary>
    /// Applies <paramref name="request"/> and reports what is painted and saved afterwards. A request
    /// it cannot carry out changes nothing and comes back with <see cref="AppThemeState.Error"/> set.
    /// </summary>
    Task<AppThemeState> HandleAsync(AppThemeRequest request);
}
