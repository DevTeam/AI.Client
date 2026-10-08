namespace AI.Web.Pages;

using System.Net.Http;
using System.Text.Json;
using AI.Contracts.Chats;
using AI.Contracts.Runs;
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

    private sealed record RecentsState(bool Folded, bool ShowMore);

    // What the Host last said about every project. The open project's own chats are live in
    // _chats and replace its part of this list, so only the other projects can be behind.
    private readonly List<ChatSummary> _recentChats = [];
    private int _recentChatsRequest;
    private bool _recentChatsStale;
    private bool _recentsFolded;
    private bool _recentsShowMore;
    private bool _recentsStateLoaded;

    // The open chat was reached through Recents: its project folds its chat list like any other
    // project, and the chat's branches show under its Recents row instead, so they are listed once.
    // Opening something from anywhere else unfolds it; with no chat open there is nothing to fold.
    private bool _projectFoldedByRecents;

    private bool IsProjectTreeFolded => _projectFoldedByRecents && GetActiveChatId() is not null;

    private void UnfoldProjectTree() => _projectFoldedByRecents = false;

    // Whether the open chat menu or rename editor belongs to a Recents row rather than the project's
    // list; a chat of the open project can be listed in both, and only one of them shows it.
    private bool _chatActionsInRecents;

    /// <summary>The most recent chats first, at most <paramref name="count"/>.</summary>
    private List<ChatSummary> GetRecentChats(int count)
    {
        var selectedProjectId = _selectedProject?.Id;
        var projectIds = _projects.Select(project => project.Id).ToHashSet();
        var openProjectChats = selectedProjectId is null
            ? []
            : _chats.Where(chat => chat.ProjectId == selectedProjectId && chat.ArchivedAt is null && !chat.IsEmpty);
        return _recentChats
            .Where(chat => chat.ProjectId != selectedProjectId && projectIds.Contains(chat.ProjectId))
            .Concat(openProjectChats)
            .OrderByDescending(chat => chat.LastActivityAt)
            .Take(count)
            .ToList();
    }

    // A Recents row is the project's chat row without the project, so the project is named here.
    private string? GetChatRowTitle(ChatSummary chat, bool inRecents)
    {
        var runTitle = GetRunTitle(chat.Id, chat.Id, GetRun(chat.Id, chat.Id));
        if (!inRecents) return runTitle;
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

    private async Task OpenRecentChatAsync(ChatSummary chat)
    {
        _projectFoldedByRecents = true;
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
        await SelectProjectAsync(project);
        if (_selectedProject?.Id == project.Id && _selectedChat?.Id != chat.Id && _chats.Any(item => item.Id == chat.Id))
            await SelectChatAsync(chat.Id);
    }

    // "Resume all" in the Recents heading: how far it has got, null while it is not running.
    private (int Done, int Total)? _recentsResumeProgress;

    /// <summary>
    /// The chats Recents shows at this moment: the first few, or the longer list once opened, and
    /// none while the section is folded.
    /// </summary>
    private List<ChatSummary> GetVisibleRecentChats(IReadOnlyList<ChatSummary> recentChats) => _recentsFolded
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
        }
        catch (JsonException)
        {
            // A damaged entry only costs the remembered layout.
        }
    }

    private Task SaveRecentsStateAsync() =>
        JsRuntime.InvokeVoidAsync("localStorage.setItem", RecentsStateKey,
            JsonSerializer.Serialize(new RecentsState(_recentsFolded, _recentsShowMore))).AsTask();
}
