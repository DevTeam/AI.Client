namespace AI.Server.Tests.Hosting;

using AI.Contracts.Settings;
using AI.Server.Hosting;
using Shouldly;
using System.Net.Http.Json;
using Xunit;

public sealed class ServerStartupTests
{
    [Fact]
    public async Task UpdatePreferencesAreHostOwnedAndForeignOriginsCannotInstall()
    {
        var token = TestContext.Current.CancellationToken;
        var directory = Path.Combine(AppContext.BaseDirectory, "startup-updates-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var composition = new StartupComposition(new ServerOptions(directory, "http://127.0.0.1:0", false, StopOnProcessSignals: false));
            await using var server = await composition.Server.StartAsync(composition, token);
            using var http = new HttpClient { BaseAddress = server.Address };
            var state = await http.GetFromJsonAsync<AI.Contracts.Updates.UpdateState>("api/updates", token);
            state.ShouldNotBeNull().Product.ShouldBe("Host");
            state.Preferences.InstallAutomatically.ShouldBeTrue();
            var preferences = new AI.Contracts.Updates.UpdatePreferences(false, false, false, AI.Contracts.Updates.UpdateChannel.Stable);
            using var save = await http.PutAsJsonAsync("api/updates/preferences", preferences, token);
            save.EnsureSuccessStatusCode();
            state = await http.GetFromJsonAsync<AI.Contracts.Updates.UpdateState>("api/updates", token);
            state.ShouldNotBeNull().Preferences.ShouldBe(preferences);
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/updates/install");
            request.Headers.Add("Origin", "https://untrusted.invalid");
            using var denied = await http.SendAsync(request, token);
            denied.StatusCode.ShouldBe(System.Net.HttpStatusCode.Forbidden);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
    [Fact]
    public async Task ProductionServiceProviderBuildsRoutesAndResolvesExternalMcpDiscovery()
    {
        var token = TestContext.Current.CancellationToken;
        var directory = Path.Combine(AppContext.BaseDirectory, "startup-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var composition = new StartupComposition(new ServerOptions(directory, "http://127.0.0.1:0", false,
                StopOnProcessSignals: false));
            await using var server = await composition.Server.StartAsync(composition, token);
            using var http = new HttpClient { BaseAddress = server.Address };
            using var health = await http.GetAsync("/api/health", token);
            health.EnsureSuccessStatusCode();
            var command = "AI.Mcp.CSharp" + (OperatingSystem.IsWindows() ? ".exe" : "");
            var settings = new McpServerSettings(Guid.NewGuid(), "CSharp", "Stdio", true, "Ask", null,
                command, [], Path.Combine(AppContext.BaseDirectory, "mcp-csharp"), [], false);
            using var response = await http.PostAsJsonAsync("/api/mcp/tools/discover",
                new DiscoverMcpToolsRequest(settings), token);
            response.EnsureSuccessStatusCode();
            var tools = await response.Content.ReadFromJsonAsync<McpToolInfo[]>(token);
            tools.ShouldNotBeNull().ShouldHaveSingleItem().Name.ShouldBe("cs_run");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
