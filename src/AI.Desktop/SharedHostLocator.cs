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
            return new SharedHostState(null, false);

        var address = new Uri("http://127.0.0.1:52173/");
        var installed = IsInstalled();
        try
        {
            using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(1) };
            using var response = client.GetAsync("api/bridge/session").GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return installed ? Incompatible() : new SharedHostState(null, false);
            var session = response.Content.ReadFromJsonAsync<SharedHost>().GetAwaiter().GetResult();
            if (session is { ProductName: HostProtocol.ProductName, ApiVersion: HostProtocol.ApiVersion })
            {
                using var page = client.GetAsync("").GetAwaiter().GetResult();
                if (page.IsSuccessStatusCode && page.Content.Headers.ContentType?.MediaType == "text/html")
                    return new SharedHostState(address, true);
                return new SharedHostState(null, true,
                    "The installed Host does not serve the Desktop interface. Reinstall AI Client Host.");
            }
            return installed ? Incompatible() : new SharedHostState(null, false);
        }
        catch (System.Text.Json.JsonException)
        {
            return installed ? Incompatible() : new SharedHostState(null, false);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            return installed
                ? new SharedHostState(null, true, "The installed Host is unavailable. Start the AI Client Host and try again.")
                : new SharedHostState(null, false);
        }
    }

    private static SharedHostState Incompatible() => new(null, true,
        "The installed Host uses a different API version. Update Host and Desktop to compatible releases.");

    private static bool IsInstalled()
    {
        if (OperatingSystem.IsWindows())
            return File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "AI Client Host", "AI.Host.exe"));
        if (OperatingSystem.IsMacOS())
            return File.Exists("/Library/LaunchAgents/org.devteam.ai-client-host.plist");
        if (OperatingSystem.IsLinux())
            return File.Exists("/usr/lib/systemd/user/ai-client-host.service");
        return false;
    }

    private sealed record SharedHost(string ProductName, int ApiVersion);
}
