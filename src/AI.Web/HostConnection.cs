namespace AI.Web;

using System.Net.Http.Json;
using Microsoft.JSInterop;

internal sealed class HostConnection(HttpClient http, IApiBaseUrl apiBaseUrl, IJSRuntime js) : IHostConnection
{
    public HostConnectionStatus Status { get; private set; } = HostConnectionStatus.Connecting;

    public HostSession? Session { get; private set; }

    public Uri Address => apiBaseUrl.Value;

    public event Action? Changed;

    public async Task ReportAsync(bool connected)
    {
        var status = connected ? HostConnectionStatus.Connected : HostConnectionStatus.Offline;
        if (status == Status && (!connected || Session is not null)) return;
        Status = status;
        if (connected)
        {
            // Read again after every reconnect: the Host may have been updated in between.
            try { Session = await http.GetFromJsonAsync<HostSession>("api/bridge/session"); }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { }
        }

        Changed?.Invoke();
    }

    public async Task<bool> DisconnectAsync()
    {
        try
        {
            using var response = await http.PostAsync("api/bridge/revoke", null);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException)
        {
            return false;
        }

        await js.InvokeVoidAsync("aiHostBridge.disconnect");
        return true;
    }
}
