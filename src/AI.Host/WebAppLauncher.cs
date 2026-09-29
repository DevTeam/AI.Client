namespace AI.Host;

using System.Net.Http.Json;
using AI.Contracts;

internal sealed class WebAppLauncher(IBrowserOpener browser) : IWebAppLauncher
{
    public async Task OpenAsync(Uri hostAddress, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = hostAddress, Timeout = TimeSpan.FromSeconds(3) };
        // Without a code the Web app still opens and explains what is wrong with the Host.
        browser.Open(await LaunchUrlAsync(client, cancellationToken) ?? HostProtocol.PublicWebOrigin + "/");
    }

    private static async Task<string?> LaunchUrlAsync(HttpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.PostAsync("api/bridge/launch", null, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            return (await response.Content.ReadFromJsonAsync<Launch>(cancellationToken))?.Url;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private sealed record Launch(string? Url);
}
