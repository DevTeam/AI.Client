namespace AI.Web.Pages;

using System.Net.Http;
using System.Text.Json;
using AI.Contracts.Chats;
using AI.Contracts.Runs;
using AI.Contracts.Schedules;
using AI.Web.Widgets;
using Microsoft.JSInterop;

/// <summary>
/// The sidebar's Recents: the chats with the latest activity across every project, under the
/// project list. A few show at first and a page more on request; the section folds away entirely.
/// </summary>
public partial class Home
{
    // How many show at first is a setting; "Show more" opens twice that, and at least ten.
    private int _recentChatCount = Settings.ClientSettings.DefaultRecentChatCount;
    private int RecentChatsShown => Math.Clamp(_recentChatCount,
        Settings.ClientSettings.MinRecentChatCount, Settings.ClientSettings.MaxRecentChatCount);
    private int RecentChatsMore => Math.Max(10, RecentChatsShown * 2);
    private const string RecentsStateKey = "ai-client.sidebar-recents.v1";

    // Scheduled's fold and page and the unfolded projects are kept with Recents': entries written
    // before them read as unfolded and as no project remembered. Fields an older entry still carries
    // (the removed Notifications section's) are ignored when it is read.
    private sealed record RecentsState(bool Folded, bool ShowMore, bool SoonFolded = false, bool SoonShowMore = false,
        Guid[]? ExpandedProjects = null);

    /// <summary>
    /// Where in the sidebar a chat row stands: the open project's list, the list of another unfolded
    /// project, Recents or Scheduled.
    /// </summary>
    private enum ChatListSurface { Project, OtherProject, Recents, Soon }

    // What the Host last said about every project. The open project's own chats are live in
    // _chats and replace its part of this list, so only the other projects can be behind.
    private readonly List<ChatSummary> _recentChats = [];
    private int _recentChatsRequest;
    private bool _recentChatsStale;
    private bool _recentsFolded;
    private bool _recentsShowMore;
    private bool _recentsStateLoaded;

    /// <summary>
    /// The row the open chat was opened from, which lists its branches: a Recents or Upcoming row
    /// keeps them where the click was, so nothing in the sidebar moves; an Upcoming row is told
    /// apart by the branch it schedules. Null, or a different chat, means its project's row.
    /// </summary>
    private (ChatListSurface Surface, Guid ChatId, Guid? SoonBranchId)? _branchHost;

    /// <summary>
    /// Where the open chat's branches are listed: under the row it was opened from while that row is
    /// on screen, otherwise under its project's row, so they are never lost with a folded section or
    /// a row pushed under "Show more".
    /// </summary>
    private ChatListSurface GetBranchHostSurface()
    {
        if (_branchHost is not { } host || host.ChatId != _selectedChat?.Id) return ChatListSurface.Project;
        return host.Surface switch
        {
            ChatListSurface.Recents when GetVisibleRecentChats(GetRecentChats(RecentChatsMore)).Any(chat => chat.Id == host.ChatId)
                => ChatListSurface.Recents,
            ChatListSurface.Soon when SoonChatsShown > 0 && GetVisibleSoonChats().Any(soon => IsBranchHost(soon))
                => ChatListSurface.Soon,
            _ => ChatListSurface.Project
        };
    }

    private bool IsBranchHost(ScheduledChatSummary soon) =>
        _branchHost is { Surface: ChatListSurface.Soon } host && host.ChatId == soon.Chat.Id && host.SoonBranchId == soon.BranchId;

    /// <summary>
    /// Whether a chat row is the open chat's. The open chat is highlighted wherever it is listed —
    /// its project and Recents alike — so the same chat looks the same in both. Under the row that
    /// lists its branches, an open branch takes the highlight from its chat.
    /// </summary>
    private bool IsOpenChatRow(ChatSummary chat, ChatListSurface surface) =>
        chat.Id == GetActiveChatId() && (surface != GetBranchHostSurface() || GetSelectedBranchId() is null);

    /// <summary>
    /// Whether an Upcoming row is the open chat's: the row listing the branches while the main
    /// conversation is open, and any other row while the branch it schedules is.
    /// </summary>
    private bool IsOpenSoonRow(ScheduledChatSummary soon)
    {
        if (soon.Chat.Id != GetActiveChatId()) return false;
        if (IsBranchHost(soon) && GetBranchHostSurface() == ChatListSurface.Soon) return GetSelectedBranchId() is null;
        return soon.BranchId is not { } branchId || branchId == soon.Chat.Id
            ? GetSelectedBranchId() is null
            : GetSelectedBranchId() == branchId;
    }

    // Which list the open chat menu or rename editor belongs to; a chat can be listed in more than
    // one, and only the row it was asked for shows it.
    private ChatListSurface _chatActionsSurface;

    /// <summary>
    /// The most recent chats of every project first, at most <paramref name="count"/>, whether or
    /// not their project is unfolded: Recents is a list of shortcuts, and a chat leaves it only when
    /// newer ones push it out, never because its project was unfolded.
    /// </summary>
    private List<ChatSummary> GetRecentChats(int count)
    {
        var selectedProjectId = _selectedProject?.Id;
        var projectIds = _projects.Select(project => project.Id).ToHashSet();
        var openProjectChats = selectedProjectId is not { } openId
            ? []
            : _chats.Where(chat => chat.ProjectId == openId && chat.ArchivedAt is null && !chat.IsEmpty);
        return _recentChats
            .Where(chat => chat.ProjectId != selectedProjectId && projectIds.Contains(chat.ProjectId))
            .Concat(openProjectChats)
            // A scheduled chat is listed by the Scheduled section instead: it is not the latest
            // thing that happened, it is work the Host is going to start on its own.
            .Where(chat => chat.Kind != ChatSchedule.Kind)
            .OrderByDescending(chat => chat.LastActivityAt)
            .Take(count)
            .ToList();
    }

    // A row outside its project's list is the project's chat row without the project, so the
    // project is named here.
    private string? GetChatRowTitle(ChatSummary chat, ChatListSurface surface)
    {
        var runTitle = GetRunTitle(chat.Id, chat.Id, GetRun(chat.Id, chat.Id));
        if (surface is ChatListSurface.Project or ChatListSurface.OtherProject) return runTitle;
        var projectName = _projects.FirstOrDefault(project => project.Id == chat.ProjectId)?.Name;
        return runTitle is null ? projectName : $"{projectName} · {runTitle}";
    }

    private async Task RefreshRecentChatsAsync()
    {
        await LoadRecentsStateAsync();
        var request = ++_recentChatsRequest;
        IReadOnlyList<ChatSummary> recent;
        try { recent = await ChatHistoryApi.ListRecentAsync(RecentChatsMore, CancellationToken.None); }
        // Unreachable Host: the event stream reports the outage itself, and the list it had stays.
        catch (HttpRequestException) { return; }
        // A slower, older answer must not replace a newer one.
        if (request != _recentChatsRequest) return;
        _recentChats.Clear();
        _recentChats.AddRange(recent);
        // The other unfolded projects' lists and the Scheduled section are part of the same read:
        // whatever changed the latest chats may have changed them too.
        await RefreshExpandedProjectChatsAsync();
        await RefreshSoonChatsAsync();
    }

    // Reading the list must not hold up whatever asked for it, a project switch above all.
    private void RefreshRecentChatsInBackground() => _ = InvokeAsync(async () =>
    {
        await RefreshRecentChatsAsync();
        StateHasChanged();
    });

    /// <summary>
    /// Whether a run event outside the open project moves that project's chats in Recents: the same
    /// moments that lift a chat in the open project's own list.
    /// </summary>
    private bool MovesRecentChat(ChatRunSnapshot? previous, ChatRunSnapshot current)
    {
        if (current.ProjectId == _selectedProject?.Id) return false;
        if (previous is null) return current.Status == ChatRunStatus.Generating;
        if (previous.Status == current.Status) return false;
        return current.Status == ChatRunStatus.Generating || previous.Status == ChatRunStatus.Generating
            || current.Status == ChatRunStatus.Failed;
    }

    /// <summary>
    /// A Recents row is a shortcut: the chat opens in its project, which is left folded or unfolded
    /// as it was, and its branches unfold under the Recents row itself.
    /// </summary>
    private async Task OpenRecentChatAsync(ChatSummary chat)
    {
        _branchHost = (ChatListSurface.Recents, chat.Id, null);
        await OpenChatFromListAsync(chat);
    }

    private Task OpenChatRowAsync(ChatSummary chat, ChatListSurface surface)
    {
        if (surface is ChatListSurface.Project or ChatListSurface.OtherProject) _branchHost = null;
        return surface switch
        {
            ChatListSurface.Recents => OpenRecentChatAsync(chat),
            ChatListSurface.OtherProject => OpenChatFromListAsync(chat),
            ChatListSurface.Soon => OpenSoonChatRowAsync(chat),
            _ => SelectChatAsync(chat.Id)
        };
    }

    /// <summary>
    /// Opens a chat from a list outside the open project's own, in its project. The project becomes
    /// the open one but keeps its fold: unfolding it would shift every row under it while the
    /// pointer is still on the list the chat was picked from.
    /// </summary>
    private async Task OpenChatFromListAsync(ChatSummary chat)
    {
        if (_selectedProject?.Id == chat.ProjectId)
        {
            await SelectChatAsync(chat.Id);
            return;
        }

        if (_projects.FirstOrDefault(project => project.Id == chat.ProjectId) is not { } project) return;
        // The project opens on the chat it remembers, in one step with the project itself, so the
        // chat is made the remembered one first; a chat that already is keeps its last branch.
        if (WorkspaceStateService.GetProjectContext(project.Id).ChatId != chat.Id)
            await WorkspaceStateService.SetProjectContextAsync(project.Id, chat.Id, null);
        await SelectProjectAsync(project, unfold: false);
        if (_selectedProject?.Id == project.Id && _selectedChat?.Id != chat.Id && _chats.Any(item => item.Id == chat.Id))
            await SelectChatAsync(chat.Id);
    }

    // "Resume all" in the Recents heading: how far it has got, null while it is not running.
    private (int Done, int Total)? _recentsResumeProgress;

    /// <summary>
    /// The chats Recents shows at this moment: the first few, or the longer list once opened, and
    /// none while the section is folded.
    /// </summary>
    private List<ChatSummary> GetVisibleRecentChats(List<ChatSummary> recentChats) => _recentsFolded
        ? []
        : recentChats.Take(_recentsShowMore ? recentChats.Count : RecentChatsShown).ToList();

    /// <summary>
    /// The stopped work of the chats Recents shows, by the unfinished-work widget's own rule: paused,
    /// interrupted, failed with Resume among its recovery actions, or idle with messages queued.
    /// Only what the person can see is offered; a chat under "Show more" or a folded section is not.
    /// </summary>
    private ChatUnfinishedTask[] GetRecentResumableTasks(IReadOnlyList<ChatSummary> visible)
    {
        var chatIds = visible.Select(chat => chat.Id).ToHashSet();
        var runs = RunState.Runs.Values.Where(run => chatIds.Contains(run.ChatId)).ToArray();
        return runs.Length == 0 ? [] : UnfinishedStatistics.Calculate(visible, runs).Tasks.Where(task => task.CanResume).ToArray();
    }

    // One after another, as the widget does it: a refusal on the first is not buried under the rest.
    // Each resume goes to the chat's own project, so nothing has to be opened first.
    private async Task ResumeRecentTasksAsync(IReadOnlyList<ChatUnfinishedTask> tasks, IReadOnlyList<ChatSummary> visible)
    {
        if (_recentsResumeProgress is not null || tasks.Count == 0) return;
        var projectIds = visible.ToDictionary(chat => chat.Id, chat => chat.ProjectId);
        _recentsResumeProgress = (0, tasks.Count);
        try
        {
            for (var index = 0; index < tasks.Count; index++)
            {
                StateHasChanged();
                var task = tasks[index];
                await RunQueueCommandAsync(() => ChatRunsApi.ResumeAsync(projectIds[task.ChatId], task.ChatId, task.BranchId,
                    Guid.CreateVersion7(), CancellationToken.None));
                _recentsResumeProgress = (index + 1, tasks.Count);
            }
        }
        finally
        {
            _recentsResumeProgress = null;
        }
    }

    private static string GetRecentsResumeTitle(int count) => count == 1
        ? "Resume the stopped task in the chats shown here"
        : $"Resume the {count} stopped tasks in the chats shown here, one after another";

    private void SetRecentChatCount(int count)
    {
        var fetched = RecentChatsMore;
        _recentChatCount = count;
        // A longer list than the Host was last asked for has to be read again.
        if (RecentChatsMore > fetched) RefreshRecentChatsInBackground();
    }

    private Task ToggleRecentsFoldedAsync()
    {
        _recentsFolded = !_recentsFolded;
        return SaveRecentsStateAsync();
    }

    private Task ToggleRecentsShowMoreAsync()
    {
        _recentsShowMore = !_recentsShowMore;
        return SaveRecentsStateAsync();
    }

    private async Task LoadRecentsStateAsync()
    {
        if (_recentsStateLoaded) return;
        _recentsStateLoaded = true;
        try
        {
            var json = await JsRuntime.InvokeAsync<string?>("localStorage.getItem", RecentsStateKey);
            if (string.IsNullOrEmpty(json) || JsonSerializer.Deserialize<RecentsState>(json) is not { } state) return;
            _recentsFolded = state.Folded;
            _recentsShowMore = state.ShowMore;
            _soonFolded = state.SoonFolded;
            _soonShowMore = state.SoonShowMore;
            // Joined, not replaced: the project opened before the entry was read stays unfolded.
            if (state.ExpandedProjects is { } expanded) _expandedProjectIds.UnionWith(expanded);
        }
        catch (JsonException)
        {
            // A damaged entry only costs the remembered layout.
        }
    }

    private Task SaveRecentsStateAsync() =>
        JsRuntime.InvokeVoidAsync("localStorage.setItem", RecentsStateKey,
            JsonSerializer.Serialize(new RecentsState(_recentsFolded, _recentsShowMore, _soonFolded, _soonShowMore,
                [.. _expandedProjectIds]))).AsTask();
}
