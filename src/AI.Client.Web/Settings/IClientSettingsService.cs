namespace AI.Client.Web.Settings;

/// <summary>The one place client preferences are read and written, persisted to localStorage.</summary>
public interface IClientSettingsService
{
    /// <summary>The saved settings, or the defaults when nothing (or nothing readable) is saved.</summary>
    ValueTask<ClientSettings> GetAsync();

    /// <summary>Applies <paramref name="update"/> to the current settings and saves the result.</summary>
    ValueTask<ClientSettings> UpdateAsync(Func<ClientSettings, ClientSettings> update);
}
