namespace AI.Desktop;

using AI.Contracts;
using System.Net.Http.Json;

internal sealed class SharedHostLocator : ISharedHostLocator
{
    public SharedHostState Find(string dataDirectory)
    {
        var sharedDataDirectory = Environment.GetEnvironmentVariable("AI_CLIENT_DATA_DIRECTORY")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetFullPath(dataDirectory), Path.GetFullPath(sharedDataDirectory), comparison))
            return new SharedHostState(null);

        var address = new Uri("http://127.0.0.1:52173/");
        try
        {
            using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(1) };
            using var response = client.GetAsync("api/bridge/session").GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return Incompatible();
            var session = response.Content.ReadFromJsonAsync<SharedHost>().GetAwaiter().GetResult();
            if (session is { ProductName: HostProtocol.ProductName, ApiVersion: HostProtocol.ApiVersion })
            {
                using var page = client.GetAsync("").GetAwaiter().GetResult();
                if (page.IsSuccessStatusCode && page.Content.Headers.ContentType?.MediaType == "text/html")
                    return new SharedHostState(address);
                return new SharedHostState(null,
                    "The installed Host does not serve the Desktop interface. Reinstall AI Client Host.");
            }
            return Incompatible();
        }
        catch (System.Text.Json.JsonException)
        {
            return Incompatible();
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            // An installed but stopped Host cannot answer. Desktop starts its embedded server;
            // the data-directory lock still prevents two processes from writing the same data.
            return new SharedHostState(null);
        }
    }

    private static SharedHostState Incompatible() => new(null,
        "The installed Host uses a different API version. Update Host and Desktop to compatible releases.", new Uri("http://127.0.0.1:52173/"));

    private sealed record SharedHost(string ProductName, int ApiVersion);
}
