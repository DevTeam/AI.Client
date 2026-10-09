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

        var address = HostAddress();
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

    /// <summary>
    /// Where the installed Host is looked for. The environment variable exists for profiling and
    /// for a machine whose usual port is taken: it names an absolute HTTP(S) address, and an
    /// absent or unusable value keeps the published one, so a mistyped variable can never stop
    /// the app from finding its Host.
    /// </summary>
    private static Uri HostAddress()
    {
        var configured = Environment.GetEnvironmentVariable("AI_CLIENT_HOST_ADDRESS");
        if (string.IsNullOrWhiteSpace(configured)
            || !Uri.TryCreate(configured.Trim(), UriKind.Absolute, out var address)
            || address.Scheme is not ("http" or "https"))
        {
            return new Uri(HostProtocol.PublicHostAddress);
        }

        // A base address without its trailing slash would let a relative path replace the last
        // segment instead of extending it.
        return address.AbsolutePath.EndsWith('/')
            ? address
            : new UriBuilder(address) { Path = address.AbsolutePath + "/" }.Uri;
    }

    private static SharedHostState Incompatible() => new(null,
        "The installed Host uses a different API version. Update Host and Desktop to compatible releases.", HostAddress());

    private sealed record SharedHost(string ProductName, int ApiVersion);
}
