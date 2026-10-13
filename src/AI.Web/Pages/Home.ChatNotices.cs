namespace AI.Web.Pages;

using AI.Web.Notifications;

/// <summary>
/// Unread notifications on the sidebar's rows. A chat or branch that a notification is about carries
/// a dot at the end of its row wherever the row is listed — its project, Recents — until it is read;
/// the bell keeps the notifications themselves. Unread follows the bell: not seen and not resolved.
/// </summary>
public partial class Home
{
    private IEnumerable<NotificationMessage> UnreadChatNotifications() => Notifications.History
        .Where(item => item is { ChatId: not null, ProjectId: not null, IsSeen: false, IsResolved: false });

    /// <summary>The dot of a chat row: its own unread notifications and its branches'; null without one.</summary>
    private string? GetChatNoticeClass(Guid chatId) =>
        UnreadChatNotifications().Where(item => item.ChatId == chatId).ToList() is { Count: > 0 } items
            ? GetNoticeClass(items)
            : null;

    /// <summary>The dot of a branch row: the branch's own unread notifications; null without one.</summary>
    private string? GetBranchNoticeClass(Guid chatId, Guid branchId) =>
        UnreadChatNotifications().Where(item => item.ChatId == chatId && (item.BranchId ?? chatId) == branchId).ToList() is { Count: > 0 } items
            ? GetNoticeClass(items)
            : null;

    // The most demanding notification decides the dot: something to do, then a failure, then news.
    private static string GetNoticeClass(IReadOnlyCollection<NotificationMessage> items)
    {
        if (items.Any(item => item.RequiresAction)) return "is-action";
        return items.Any(item => item.Kind == NotificationKind.Error) ? "is-error" : "is-info";
    }
}
