using Pure.DI;

namespace AI.Web.Notifications;

/// <summary>Preserves the notification store while mirroring count changes to the desktop host.</summary>
internal sealed class DesktopBadgeNotificationService(
    [Tag("base")] INotificationService baseNotificationService,
    IUnreadCountPublisher publisher)
    : INotificationService, IDisposable
{
    private int _queuedCount = -1;
    private Task _lastPublish = Task.CompletedTask;
    private bool _subscribed;

    public NotificationMessage? Current => baseNotificationService.Current;
    public IReadOnlyList<NotificationMessage> History => baseNotificationService.History;
    public int UnreadCount => baseNotificationService.UnreadCount;

    public event Action? Changed;
    public event Action<NotificationMessage>? OpenRequested
    {
        add => baseNotificationService.OpenRequested += value;
        remove => baseNotificationService.OpenRequested -= value;
    }

    public event Action? CenterRequested
    {
        add => baseNotificationService.CenterRequested += value;
        remove => baseNotificationService.CenterRequested -= value;
    }

    public async Task InitializeAsync()
    {
        if (!_subscribed)
        {
            baseNotificationService.Changed += OnChanged;
            _subscribed = true;
        }
        await baseNotificationService.InitializeAsync();
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
        var count = baseNotificationService.UnreadCount;
        if (_queuedCount == count) return;
        _queuedCount = count;
        _lastPublish = publisher.PublishAsync(count);
    }

    public void Open(NotificationMessage message) => baseNotificationService.Open(message);
    public void OpenCenter() => baseNotificationService.OpenCenter();
    public void MarkAllSeen() => baseNotificationService.MarkAllSeen();
    public void MarkSeen(Guid id) => baseNotificationService.MarkSeen(id);
    public void MarkChatBranchSeen(Guid chatId, Guid branchId) => baseNotificationService.MarkChatBranchSeen(chatId, branchId);
    public void ResolveChatAttention(Guid chatId, Guid branchId) => baseNotificationService.ResolveChatAttention(chatId, branchId);
    public void ShowChatEvent(string message, NotificationKind kind, Guid projectId, Guid chatId, Guid branchId, bool requiresAction = false, Guid? messageId = null) =>
        baseNotificationService.ShowChatEvent(message, kind, projectId, chatId, branchId, requiresAction, messageId);
    public void ShowSuccess(string message) => baseNotificationService.ShowSuccess(message);
    public void ShowError(string message) => baseNotificationService.ShowError(message);
    public void ShowInfo(string message) => baseNotificationService.ShowInfo(message);
    public void ShowUpdate(string message, NotificationKind kind) => baseNotificationService.ShowUpdate(message, kind);
    public void Dismiss() => baseNotificationService.Dismiss();
    public void PauseAutoDismiss() => baseNotificationService.PauseAutoDismiss();
    public void ResumeAutoDismiss() => baseNotificationService.ResumeAutoDismiss();

    public void Dispose()
    {
        if (_subscribed) baseNotificationService.Changed -= OnChanged;
    }
}
