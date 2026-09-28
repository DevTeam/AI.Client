namespace AI.Host;

using System.CommandLine;
using System.Diagnostics;
using System.Net.Http.Json;
using AI.Contracts;
using Server.CommandLine;

/// <summary>
/// <c>AI.Host open</c>, run by the installer and the Start menu: starts the Host if it is not
/// running, then opens the Web app with a one-time pairing code so the browser connects without
/// any further question.
/// </summary>
internal sealed class OpenCommand(RootCommand rootCommand, IHostProcess hostProcess, IBrowserOpener browser) : IInitializable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        var command = new Command("open", "Start the Host if needed and open AI Client in the default browser, connected to this Host.");
        command.SetAction((_, token) => RunAsync(token));
        rootCommand.Subcommands.Add(command);
        return Task.CompletedTask;
    }

    private async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = new Uri(HostProtocol.PublicHostAddress), Timeout = TimeSpan.FromSeconds(3) };
        if (!await IsRunningAsync(client, cancellationToken))
        {
            hostProcess.StartInBackground();
            var waiting = Stopwatch.StartNew();
            while (waiting.Elapsed < StartTimeout && !await IsRunningAsync(client, cancellationToken))
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        // Without a code the Web app still opens and explains what is wrong with the Host.
        browser.Open(await LaunchUrlAsync(client, cancellationToken) ?? HostProtocol.PublicWebOrigin + "/");
        return 0;
    }

    private static async Task<bool> IsRunningAsync(HttpClient client, CancellationToken cancellationToken)
    {
        try
        {
            var health = await client.GetFromJsonAsync<Health>("api/health", cancellationToken);
            return health?.ProductName == HostProtocol.ProductName;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return false;
        }
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

    private sealed record Health(string? ProductName);

    private sealed record Launch(string? Url);
}
