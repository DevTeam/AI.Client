namespace AI.Web.Pages;

using System.Net.Http;
using AI.Contracts.Chats;
using AI.Web.Notifications;

/// <summary>
/// The sidebar's Notifications: every chat with an unread notification, on the chat itself or on
/// one of its branches, across projects, the newest notification first. The branches the
/// notifications are about stand under their chat, so a fork that finished or failed is opened
/// without opening its chat first. Unread follows the bell: not seen and not resolved.
/// </summary>
public partial class Home
{
    /// <param name="Chat">The chat as the sidebar knows it.</param>
    /// <param name="MainNotice">The dot for an unread notification on the main line, null without one.</param>
    /// <param name="Branches">Forks with unread notifications, the newest first.</param>
    /// <param name="Unread">Unread notifications of the chat and its forks together.</param>
    /// <param name="Newest">When the newest of them arrived; the list is ordered by it.</param>
    private sealed record NotifiedChat(ChatSummary Chat, string? MainNotice, IReadOnlyList<NotifiedBranch> Branches,
        int Unread, DateTimeOffset Newest);

    private sealed record NotifiedBranch(Guid Id, string Title, string Notice);

    private bool _notifiedFolded;
    private bool _notifiedShowMore;

    // Chats of other projects that neither Recents nor the open project list, and the branch lists
    // of chats that are not open: read from the Host only for what a notification names.
    private readonly Dictionary<Guid, ChatSummary> _notifiedChatSummaries = [];
    private readonly Dictionary<Guid, IReadOnlyList<ChatBranchView>> _notifiedChatBranches = [];
    // What was already asked for, so a chat or branch that is gone is not asked for on every change.
    private readonly HashSet<Guid> _notifiedChatsTried = [];
    private readonly HashSet<(Guid ChatId, Guid BranchId)> _notifiedBranchesTried = [];
    private bool _notifiedLoading;

    private IEnumerable<NotificationMessage> UnreadChatNotifications() => Notifications.History
        .Where(item => item is { ChatId: not null, ProjectId: not null, IsSeen: false, IsResolved: false });

    /// <summary>
    /// The chats Notifications lists. The chat opened from it stays listed while it is open, even
    /// once opening it has read its notifications, so the row and its branches do not vanish
    /// from under the pointer.
    /// </summary>
    private List<NotifiedChat> GetNotifiedChats()
    {
        var unread = UnreadChatNotifications().ToList();
        var openedHere = IsFoldedInto(ChatListSurface.Notifications) ? GetActiveChatId() : null;
        if (unread.Count == 0 && openedHere is null) return [];
        var projectIds = _projects.Select(project => project.Id).ToHashSet();
        var result = new List<NotifiedChat>();
        var chatIds = unread.Select(item => item.ChatId!.Value).Distinct().ToList();
        if (openedHere is { } opened && !chatIds.Contains(opened)) chatIds.Add(opened);
        foreach (var chatId in chatIds)
        {
            var items = unread.Where(item => item.ChatId == chatId).ToList();
            var projectId = items.FirstOrDefault()?.ProjectId ?? _selectedProject?.Id;
            if (projectId is not { } chatProjectId || !projectIds.Contains(chatProjectId)) continue;
            if (FindChatSummary(chatId, chatProjectId) is not { ArchivedAt: null } chat) continue;
            var main = items.Where(item => (item.BranchId ?? chatId) == chatId).ToList();
            var branches = FindChatBranches(chatId);
            var forks = items.Where(item => (item.BranchId ?? chatId) != chatId)
                .GroupBy(item => item.BranchId!.Value)
                .Select(group => (Group: group, Branch: branches?.FirstOrDefault(branch => branch.Id == group.Key)))
                // A fork whose branch is not known (yet, or any more) is left out rather than shown nameless.
                .Where(pair => pair.Branch is { HeadMessageId: not null })
                .OrderByDescending(pair => pair.Group.Max(item => item.CreatedAt))
                .Select(pair => new NotifiedBranch(pair.Group.Key, pair.Branch!.Title, GetNoticeClass(pair.Group)))
                .ToList();
            // The open chat with nothing unread left orders by the last notification it had.
            var newest = items.Count > 0 ? items.Max(item => item.CreatedAt)
                : Notifications.History.Where(item => item.ChatId == chatId).Select(item => item.CreatedAt).DefaultIfEmpty().Max();
            result.Add(new NotifiedChat(chat, main.Count > 0 ? GetNoticeClass(main) : null, forks, items.Count, newest));
        }
        return result.OrderByDescending(item => item.Newest).ToList();
    }

    // The most demanding notification decides the dot: something to do, then a failure, then news.
    private static string GetNoticeClass(IEnumerable<NotificationMessage> items)
    {
        var list = items as IReadOnlyCollection<NotificationMessage> ?? items.ToList();
        if (list.Any(item => item.RequiresAction)) return "is-action";
        return list.Any(item => item.Kind == NotificationKind.Error) ? "is-error" : "is-info";
    }

    private ChatSummary? FindChatSummary(Guid chatId, Guid projectId) =>
        projectId == _selectedProject?.Id
            ? _chats.FirstOrDefault(chat => chat.Id == chatId)
            : _recentChats.FirstOrDefault(chat => chat.Id == chatId) ?? _notifiedChatSummaries.GetValueOrDefault(chatId);

    private IReadOnlyList<ChatBranchView>? FindChatBranches(Guid chatId) =>
        _selectedChat?.Id == chatId ? _selectedChat.Branches
            : _chatDetailsCache.GetValueOrDefault(chatId)?.Branches ?? _notifiedChatBranches.GetValueOrDefault(chatId);

    /// <summary>
    /// Reads what the list needs and does not have: the summaries of chats in other projects, and
    /// the branches of chats whose forks have notifications. Returns whether anything was read.
    /// </summary>
    private async Task<bool> LoadNotifiedChatDataAsync()
    {
        if (_notifiedLoading) return false;
        _notifiedLoading = true;
        var changed = false;
        try
        {
            var unread = UnreadChatNotifications().ToList();
            var missingProjects = unread
                .Where(item => item.ProjectId != _selectedProject?.Id && _projects.Any(project => project.Id == item.ProjectId)
                    && FindChatSummary(item.ChatId!.Value, item.ProjectId!.Value) is null && _notifiedChatsTried.Add(item.ChatId!.Value))
                .Select(item => item.ProjectId!.Value).Distinct().ToList();
            foreach (var projectId in missingProjects)
            {
                foreach (var chat in await ChatHistoryApi.ListAsync(projectId, CancellationToken.None))
                    _notifiedChatSummaries[chat.Id] = chat;
                changed = true;
            }

            var missingBranches = unread
                .Where(item => item.BranchId is { } branchId && branchId != item.ChatId
                    && FindChatBranches(item.ChatId!.Value)?.Any(branch => branch.Id == branchId) != true
                    && _notifiedBranchesTried.Add((item.ChatId!.Value, branchId)))
                .Select(item => (ChatId: item.ChatId!.Value, ProjectId: item.ProjectId!.Value)).Distinct().ToList();
            foreach (var (chatId, projectId) in missingBranches)
            {
                if (await ChatHistoryApi.GetTranscriptAsync(projectId, chatId, CancellationToken.None) is { Branches: { } branches })
                    _notifiedChatBranches[chatId] = branches;
                changed = true;
            }
        }
        // Unreachable Host: the event stream reports the outage; the rows wait for the next change.
        catch (HttpRequestException) { }
        finally
        {
            _notifiedLoading = false;
        }
        return changed;
    }

    private void LoadNotifiedChatDataInBackground() => _ = InvokeAsync(async () =>
    {
        if (await LoadNotifiedChatDataAsync()) StateHasChanged();
    });

    /// <summary>
    /// The chat row: the main line when the notification is about it, otherwise the chat as
    /// Recents opens it; its forks have rows of their own.
    /// </summary>
    private Task OpenNotifiedChatRowAsync(ChatSummary chat) =>
        UnreadChatNotifications().Any(item => item.ChatId == chat.Id && (item.BranchId ?? chat.Id) == chat.Id)
            ? OpenNotifiedBranchAsync(chat, chat.Id)
            : OpenChatFromListAsync(chat, ChatListSurface.Notifications);

    /// <summary>
    /// Opens the branch a notification is about at the message it names, the way the bell opens
    /// it, and reads that branch's notifications. The project folds into Notifications.
    /// </summary>
    private async Task OpenNotifiedBranchAsync(ChatSummary chat, Guid branchId)
    {
        _projectFoldedBy = ChatListSurface.Notifications;
        var newest = UnreadChatNotifications()
            .Where(item => item.ChatId == chat.Id && (item.BranchId ?? chat.Id) == branchId)
            .MaxBy(item => item.CreatedAt);
        if (_selectedProject?.Id != chat.ProjectId)
        {
            if (_projects.FirstOrDefault(project => project.Id == chat.ProjectId) is not { } project) return;
            // As from Recents: the project opens on this chat and branch in one step.
            await WorkspaceStateService.SetProjectContextAsync(project.Id, chat.Id, null, branchId);
            await SelectProjectAsync(project);
        }
        await NavigateToTargetAsync(new WorkspaceTarget(chat.ProjectId, chat.Id, branchId, newest?.MessageId, false));
        Notifications.MarkChatBranchSeen(chat.Id, branchId);
    }

    private Task ToggleNotifiedFoldedAsync()
    {
        _notifiedFolded = !_notifiedFolded;
        return SaveRecentsStateAsync();
    }

    private Task ToggleNotifiedShowMoreAsync()
    {
        _notifiedShowMore = !_notifiedShowMore;
        return SaveRecentsStateAsync();
    }
}
