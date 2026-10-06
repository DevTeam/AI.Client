namespace AI.Web.Notifications;

/// <summary>
/// App-wide notifications: a transient corner popup and a bounded history that can be reopened.
/// The popup shows the latest event and a short tail; the history survives a page reload.
/// </summary>
public interface INotificationService
{
    /// <summary>The currently visible notification, or null if none is shown.</summary>
    NotificationMessage? Current { get; }

    IReadOnlyList<NotificationMessage> History { get; }

    int UnreadCount { get; }

    Task InitializeAsync();

    void ShowChatEvent(string message, NotificationKind kind, Guid projectId, Guid chatId, Guid branchId, bool requiresAction = false, Guid? messageId = null);

    void MarkAllSeen();

    void MarkSeen(Guid id);

    void MarkChatBranchSeen(Guid chatId, Guid branchId);

    void ResolveChatAttention(Guid chatId, Guid branchId);

    /// <summary>Raised when the visible notification changes. Subscribers re-render in response.</summary>
    event Action? Changed;

    event Action<NotificationMessage>? OpenRequested;

    event Action? CenterRequested;

    void Open(NotificationMessage message);

    void OpenCenter();

    /// <summary>Shows a success notification (green border).</summary>
    void ShowSuccess(string message);

    /// <summary>Shows an error notification (red border). The message is meant for end users, not logs.</summary>
    void ShowError(string message);

    /// <summary>Shows an informational notification (blue border).</summary>
    void ShowInfo(string message);
    void ShowUpdate(string message, NotificationKind kind);

    /// <summary>
    /// Removes the current notification immediately. Callers rarely need this — the auto-dismiss
    /// timer handles the normal case — but Escape in Home.razor still calls it so a quick keypress
    /// can clear the corner without waiting.
    /// </summary>
    void Dismiss();

    void PauseAutoDismiss();

    void ResumeAutoDismiss();
}

/// <summary>
/// One event in the notification history and, briefly, the corner popup.
/// </summary>
/// <param name="Message">The text shown to the user.</param>
/// <param name="Kind">success, error or info — drives the border colour and the leading icon.</param>
public sealed record NotificationMessage(
    string Message,
    NotificationKind Kind,
    Guid Id = default,
    DateTimeOffset CreatedAt = default,
    Guid? ProjectId = null,
    Guid? ChatId = null,
    Guid? BranchId = null,
    Guid? MessageId = null,
    bool IsSeen = false,
    bool RequiresAction = false,
    bool IsResolved = false,
    bool OpenUpdates = false);

public enum NotificationKind
{
    Success,
    Error,
    Info
}
