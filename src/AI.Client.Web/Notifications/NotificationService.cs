namespace AI.Client.Web.Notifications;

using System.Text.Json;
using Microsoft.JSInterop;

/// <summary>
/// App-wide notification store. The latest event is shown briefly; a bounded history is kept
/// in browser storage so dismissing a popup never discards the event.
/// </summary>
public sealed class NotificationService(IJSRuntime jsRuntime) : INotificationService, IDisposable
{
    // Same four seconds Home.razor's old in-place toast used. Tuned for a glance, not for reading —
    // Longer explanations remain available in the history drawer.
    private static readonly TimeSpan AutoDismissAfter = TimeSpan.FromSeconds(4);
    private const string StorageKey = "ai-client.notifications.v1";
    private const int MaxHistory = 100;

    private CancellationTokenSource? _autoDismissCts;
    private readonly List<NotificationMessage> _history = [];
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private bool _initialized;
    private bool _hovered;

    public NotificationMessage? Current { get; private set; }
    public IReadOnlyList<NotificationMessage> History => _history;
    public int UnreadCount => _history.Count(item => item.ChatId is not null && !item.IsSeen && !item.IsResolved);

    public event Action? Changed;
    public event Action<NotificationMessage>? OpenRequested;
    public event Action? CenterRequested;

    public void OpenCenter() => CenterRequested?.Invoke();

    public void Open(NotificationMessage message)
    {
        if (message.ChatId is null) return;
        OpenRequested?.Invoke(message);
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            var json = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrWhiteSpace(json))
                _history.AddRange((JsonSerializer.Deserialize<List<NotificationMessage>>(json) ?? []).Take(MaxHistory));
        }
        catch (Exception error) when (error is JsonException or JSException)
        {
            _history.Clear();
        }
        Changed?.Invoke();
    }

    public void ShowSuccess(string message) => Show(new NotificationMessage(message, NotificationKind.Success));

    public void ShowError(string message) => Show(new NotificationMessage(message, NotificationKind.Error));

    public void ShowInfo(string message) => Show(new NotificationMessage(message, NotificationKind.Info));

    public void ShowChatEvent(string message, NotificationKind kind, Guid projectId, Guid chatId, Guid branchId, bool requiresAction = false, Guid? messageId = null) =>
        Show(new NotificationMessage(message, kind, ProjectId: projectId, ChatId: chatId, BranchId: branchId, MessageId: messageId, RequiresAction: requiresAction));

    public void MarkSeen(Guid id)
    {
        var index = _history.FindIndex(item => item.Id == id);
        if (index < 0 || _history[index].IsSeen) return;
        _history[index] = _history[index] with { IsSeen = true };
        Changed?.Invoke();
        _ = PersistAsync();
    }

    public void MarkAllSeen()
    {
        if (_history.All(item => item.IsSeen)) return;
        for (var index = 0; index < _history.Count; index++) _history[index] = _history[index] with { IsSeen = true };
        Changed?.Invoke();
        _ = PersistAsync();
    }

    public void ResolveChatAttention(Guid chatId, Guid branchId)
    {
        var changed = false;
        for (var index = 0; index < _history.Count; index++)
        {
            var item = _history[index];
            if (!item.RequiresAction || item.IsResolved || item.ChatId != chatId || item.BranchId != branchId) continue;
            _history[index] = item with { IsResolved = true };
            changed = true;
        }
        if (!changed) return;
        Changed?.Invoke();
        _ = PersistAsync();
    }

    public void Dismiss()
    {
        if (Current is null) return;
        CancelAutoDismiss();
        Current = null;
        Changed?.Invoke();
    }

    public void PauseAutoDismiss()
    {
        _hovered = true;
        CancelAutoDismiss();
    }

    public void ResumeAutoDismiss()
    {
        _hovered = false;
        if (Current is not null) StartAutoDismiss();
    }

    private void Show(NotificationMessage message)
    {
        // Replacing the previous message always cancels its dismiss timer: otherwise a constant
        // stream of toasts (every save, every queue command) would leave the new message auto-clearing
        // by the OLD timer, popping the corner mid-glance. The new timer is owned by the new message.
        CancelAutoDismiss();
        Current = message with { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        _history.Insert(0, Current);
        if (_history.Count > MaxHistory) _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);
        Changed?.Invoke();
        _ = PersistAsync();

        if (!_hovered) StartAutoDismiss();
    }

    private void StartAutoDismiss()
    {
        CancelAutoDismiss();
        var cts = new CancellationTokenSource();
        _autoDismissCts = cts;
        _ = AutoDismissAsync(cts);
    }

    private async Task PersistAsync()
    {
        await _saveGate.WaitAsync();
        try
        {
            await jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(_history));
        }
        catch (JSException)
        {
            // Notifications remain available for this session if browser storage is disabled.
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private async Task AutoDismissAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(AutoDismissAfter, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Only clear if this timer is still the active one. A Show() that arrived during the delay
        // swapped _autoDismissCts to a newer instance; clearing Current now would wipe that newer
        // message and pop the corner mid-glance.
        if (!ReferenceEquals(_autoDismissCts, cts)) return;
        _autoDismissCts = null;
        Current = null;
        Changed?.Invoke();
    }

    private void CancelAutoDismiss()
    {
        _autoDismissCts?.Cancel();
        _autoDismissCts?.Dispose();
        _autoDismissCts = null;
    }

    public void Dispose()
    {
        CancelAutoDismiss();
        _saveGate.Dispose();
    }
}
