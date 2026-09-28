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
        // The status is shown before anything else is asked of the Host, so a slow or failing
        // session read below can never leave the dot showing the previous state.
        Status = status;
        Changed?.Invoke();
        if (!connected) return;

        // Read again after every reconnect: the Host may have been updated in between. Only the
        // version and the Desktop hint depend on it, so any failure just leaves them as they were.
        try
        {
            Session = await http.GetFromJsonAsync<HostSession>("api/bridge/session") ?? Session;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Host session was not read: {error.Message}");
            return;
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
