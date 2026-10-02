namespace AI.Desktop;

using AI.Updates;
using System.Net.Http.Json;

internal sealed class DesktopUpdates : IDesktopUpdates
{
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    public IUpdateManager Manager { get; }
    public event Action? ShutdownRequested;

    public DesktopUpdates(DesktopStart start, IUpdateManagerFactory factory)
    {
        _http = new HttpClient { BaseAddress = start.Address ?? start.HostUpdateAddress, Timeout = TimeSpan.FromMinutes(30) };
        Manager = factory.Create("Desktop", start.DataDirectory,
            TryEnterMaintenanceAsync,
            () => _ = ResumeAsync(), () => ShutdownRequested?.Invoke());
    }

    public void Start() => _worker ??= Manager.RunAsync(_stop.Token);

    private async Task<bool> TryEnterMaintenanceAsync(CancellationToken token)
    {
        using var response = await _http.PostAsync("api/updates/desktop-idle", null, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<bool>(token);
    }

    private async Task ResumeAsync()
    {
        try { using var response = await _http.PostAsync("api/updates/desktop-resume", null, _stop.Token); }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_worker is not null) await _worker;
        await Manager.DisposeAsync();
        _http.Dispose();
        _stop.Dispose();
    }
}
