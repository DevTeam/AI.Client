namespace AI.Web.Updates;

using AI.Contracts.Updates;
using AI.Web.Notifications;
using AI.Web.State;
using Microsoft.JSInterop;
using System.Net.Http.Json;
using System.Text.Json;

public sealed class UpdateClient(HttpClient http, IJSRuntime js, INotificationService notifications,
    IWorkspaceStateService workspace) : IUpdateClient, IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _initialization = new(1);
    private Task? _poll;
    private string? _lastError;
    private bool _flushed;
    private bool _hostWasInstalling;
    public UpdateState? State { get; private set; }
    public bool IsDesktop { get; private set; }
    public bool Reconnecting { get; private set; }
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        await _initialization.WaitAsync(_stop.Token);
        try
        {
            if (_poll is not null) return;
            IsDesktop = await js.InvokeAsync<bool>("aiClientUpdates.isDesktop");
            await notifications.InitializeAsync();
            await RefreshAsync();
            _poll = PollAsync();
        }
        finally { _initialization.Release(); }
    }

    public async Task ExecuteAsync(string operation, UpdatePreferences? preferences = null)
    {
        try
        {
            if (IsDesktop)
                State = await js.InvokeAsync<UpdateState>("aiClientUpdates.request", _stop.Token, operation, preferences);
            else
            {
                using var response = preferences is null
                    ? await http.PostAsync("api/updates/" + operation, null, _stop.Token)
                    : await http.PutAsJsonAsync("api/updates/preferences", preferences, _stop.Token);
                response.EnsureSuccessStatusCode();
                State = await response.Content.ReadFromJsonAsync<UpdateState>(_stop.Token);
            }
            await ObserveAsync();
        }
        catch (Exception error) when (error is HttpRequestException or JSException or JsonException or TaskCanceledException)
        {
            notifications.ShowUpdate("The update request failed. Try again when the application is connected.", NotificationKind.Error);
        }
        Changed?.Invoke();
    }

    private async Task PollAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _stop.Token);
                await RefreshAsync();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var previous = State;
            State = IsDesktop
                ? await js.InvokeAsync<UpdateState>("aiClientUpdates.request", _stop.Token, "state", null)
                : await http.GetFromJsonAsync<UpdateState>("api/updates", _stop.Token);
            Reconnecting = false;
            await ObserveAsync();
            if (previous != State) Changed?.Invoke();
            if (_hostWasInstalling && State is { Phase: not UpdatePhase.Installing })
            {
                _hostWasInstalling = false;
                await workspace.FlushPendingComposerDraftAsync();
                // The Host serves this UI; load the newly installed static assets as well.
                await js.InvokeVoidAsync("aiClientUpdates.reload");
            }
        }
        catch (Exception error) when (error is HttpRequestException or JSException or JsonException or TaskCanceledException)
        {
            if (_stop.IsCancellationRequested) return;
            Reconnecting = State?.Phase == UpdatePhase.Installing;
            Changed?.Invoke();
        }
    }

    private async Task ObserveAsync()
    {
        if (State is null) return;
        if (State.Release is { } release && State.Phase is not UpdatePhase.Checking)
            await NotifyOnceAsync("available", release.Version, $"A new {State.Product} version is available: {release.Version}", NotificationKind.Info);
        if (State.InstalledVersion is { } installed)
            await NotifyOnceAsync("installed", installed, $"{State.Product} was updated to {installed}", NotificationKind.Success);
        if (State.Phase == UpdatePhase.Failed && State.Error is { } error && error != _lastError)
            notifications.ShowUpdate($"{State.Product} update failed: {error}", NotificationKind.Error);
        _lastError = State.Error;
        if (State.Phase == UpdatePhase.Installing)
        {
            _hostWasInstalling = !IsDesktop;
            if (!_flushed)
            {
                await js.InvokeVoidAsync("aiClientUpdates.freeze", true);
                await workspace.FlushPendingComposerDraftAsync();
                _flushed = true;
            }
        }
        else if (_flushed)
        {
            await js.InvokeVoidAsync("aiClientUpdates.freeze", false);
            _flushed = false;
        }
    }

    private async Task NotifyOnceAsync(string kind, string version, string message, NotificationKind notificationKind)
    {
        var key = $"ai-client.update.{State!.Product}.{kind}.{version}";
        if (await js.InvokeAsync<string?>("localStorage.getItem", key) is not null) return;
        notifications.ShowUpdate(message, notificationKind);
        await js.InvokeVoidAsync("localStorage.setItem", key, "1");
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_poll is not null) await _poll;
        _initialization.Dispose();
        _stop.Dispose();
    }
}
