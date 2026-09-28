namespace AI.Web.Settings;

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

public sealed class ClientSettingsService(IJSRuntime jsRuntime) : IClientSettingsService
{
    // js/theme.js reads this entry before Blazor starts; on Desktop it first restores the entry
    // from the profile because the embedded server's port, and thus the localStorage origin, changes.
    internal const string StorageKey = "ai-client.settings";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private ClientSettings? _current;

    public async ValueTask<ClientSettings> GetAsync() => _current ??= await ReadAsync();

    public async ValueTask<ClientSettings> UpdateAsync(Func<ClientSettings, ClientSettings> update)
    {
        var next = update(await GetAsync());
        await jsRuntime.InvokeVoidAsync("aiClientTheme.saveClientSettings", JsonSerializer.Serialize(next, JsonOptions));
        _current = next;
        return next;
    }

    private async Task<ClientSettings> ReadAsync()
    {
        var json = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        if (string.IsNullOrWhiteSpace(json)) return new ClientSettings();
        try
        {
            return JsonSerializer.Deserialize<ClientSettings>(json, JsonOptions) ?? new ClientSettings();
        }
        catch (JsonException)
        {
            // Hand-edited or written by a build that knew a value this one does not: start from
            // the defaults rather than keep the app from opening its settings.
            return new ClientSettings();
        }
    }
}
