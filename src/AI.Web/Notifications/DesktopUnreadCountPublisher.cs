namespace AI.Web.Notifications;

using Microsoft.JSInterop;

/// <summary>Sends the unread count to an Avalonia host when this page runs inside one.</summary>
internal sealed class DesktopUnreadCountPublisher(IJSRuntime jsRuntime) : IUnreadCountPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IJSObjectReference? _module;
    private int _lastCount = -1;

    public async Task PublishAsync(int count)
    {
        await _gate.WaitAsync();
        try
        {
            if (_lastCount == count) return;
            _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/desktopBadge.js");
            await _module.InvokeVoidAsync("publishUnreadCount", count);
            _lastCount = count;
        }
        catch (Exception error) when (error is JSException or ObjectDisposedException)
        {
            // A browser has no desktop badge; the notifications remain available in the page.
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); }
            catch (JSException) { /* The page may already be shutting down. */ }
        }
        _gate.Dispose();
    }
}
