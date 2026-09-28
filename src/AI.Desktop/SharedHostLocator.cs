namespace AI.Desktop;

using System.Net.Http.Json;

internal sealed class SharedHostLocator : ISharedHostLocator
{
    public SharedHostState Find()
    {
        var address = new Uri("http://127.0.0.1:52173/");
        var installed = IsInstalled();
        try
        {
            using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(1) };
            var session = client.GetFromJsonAsync<SharedHost>("api/bridge/session").GetAwaiter().GetResult();
            if (session is { ProductName: "AI Client", ApiVersion: 1 })
                return new SharedHostState(address, true);
            return installed
                ? new SharedHostState(null, true, "The installed Host is not compatible with this Desktop version.")
                : new SharedHostState(null, false);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return installed
                ? new SharedHostState(null, true, "The installed Host is unavailable. Start the AI Client Host and try again.")
                : new SharedHostState(null, false);
        }
    }

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
