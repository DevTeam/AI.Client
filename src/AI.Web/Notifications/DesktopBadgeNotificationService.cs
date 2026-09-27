namespace AI.Web.Notifications;

/// <summary>Preserves the notification store while mirroring count changes to the desktop host.</summary>
internal sealed class DesktopBadgeNotificationService(NotificationService inner, IUnreadCountPublisher publisher)
    : INotificationService, IDisposable
{
    private int _queuedCount = -1;
    private Task _lastPublish = Task.CompletedTask;
    private bool _subscribed;

    public NotificationMessage? Current => inner.Current;
    public IReadOnlyList<NotificationMessage> History => inner.History;
    public int UnreadCount => inner.UnreadCount;

    public event Action? Changed;
    public event Action<NotificationMessage>? OpenRequested
    {
        add => inner.OpenRequested += value;
        remove => inner.OpenRequested -= value;
    }
    public event Action? CenterRequested
    {
        add => inner.CenterRequested += value;
        remove => inner.CenterRequested -= value;
    }

    public async Task InitializeAsync()
    {
        if (!_subscribed)
        {
            inner.Changed += OnChanged;
            _subscribed = true;
        }
        await inner.InitializeAsync();
        if (_queuedCount < 0) QueueCount();
        await _lastPublish;
    }

    private void OnChanged()
    {
        QueueCount();
        Changed?.Invoke();
    }

    private void QueueCount()
    {
        var count = inner.UnreadCount;
        if (_queuedCount == count) return;
        _queuedCount = count;
        _lastPublish = publisher.PublishAsync(count);
    }

    public void Open(NotificationMessage message) => inner.Open(message);
    public void OpenCenter() => inner.OpenCenter();
    public void MarkAllSeen() => inner.MarkAllSeen();
    public void MarkSeen(Guid id) => inner.MarkSeen(id);
    public void ResolveChatAttention(Guid chatId, Guid branchId) => inner.ResolveChatAttention(chatId, branchId);
    public void ShowChatEvent(string message, NotificationKind kind, Guid projectId, Guid chatId, Guid branchId, bool requiresAction = false, Guid? messageId = null) =>
        inner.ShowChatEvent(message, kind, projectId, chatId, branchId, requiresAction, messageId);
    public void ShowSuccess(string message) => inner.ShowSuccess(message);
    public void ShowError(string message) => inner.ShowError(message);
    public void ShowInfo(string message) => inner.ShowInfo(message);
    public void Dismiss() => inner.Dismiss();
    public void PauseAutoDismiss() => inner.PauseAutoDismiss();
    public void ResumeAutoDismiss() => inner.ResumeAutoDismiss();

    public void Dispose()
    {
        if (_subscribed) inner.Changed -= OnChanged;
    }
}
