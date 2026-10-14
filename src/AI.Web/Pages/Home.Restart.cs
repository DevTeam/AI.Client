namespace AI.Web.Pages;

using System.Net.Http;
using AI.Contracts.Chats;
using AI.Contracts.Runs;
using AI.Web.Settings;
using AI.Web.Widgets;

/// <summary>
/// The work a restart of the application stopped. The Host stamps every run one start recovered
/// with the same moment; until this device has dealt with that start, its runs are a group of their
/// own in Unfinished work and, while the open chat is not the only one of them, a pill above the
/// composer points there. Settings → Chat → After a restart chooses whether the client asks,
/// resumes after a countdown or says nothing.
/// </summary>
public partial class Home
{
    private RestartRecoveryMode _afterRestart = RestartRecoveryMode.Ask;
    private DateTimeOffset? _restartSeenAt;
    // The start an automatic resume is counting down for, and when it fires.
    private DateTimeOffset? _autoResumeFor;
    private DateTimeOffset? _autoResumeAt;
    // A countdown cancelled once is not started again for the same start.
    private DateTimeOffset? _autoResumeCancelledFor;
    private (int Done, int Total)? _restartResumeProgress;
    // Chats of projects the sidebar has not listed, read only to name the rows of the restart group.
    private readonly Dictionary<Guid, ChatSummary> _restartChats = [];
    private readonly HashSet<Guid> _restartProjectsRead = [];
    private bool _revealRestartWork;

    private void LoadRestartSettings(ClientSettings settings)
    {
        _afterRestart = settings.AfterRestart;
        _restartSeenAt = settings.RestartSeenAt;
    }

    private void SetAfterRestart(RestartRecoveryMode mode)
    {
        _afterRestart = mode;
        // A countdown under way stops when the person no longer wants one.
        if (mode != RestartRecoveryMode.ResumeAutomatically) _autoResumeAt = null;
    }

    /// <summary>Every chat the page knows, of any project: the open project's, the sidebar's other lists and the ones read for the restart group.</summary>
    private List<ChatSummary> GetKnownChats()
    {
        var known = new Dictionary<Guid, ChatSummary>();
        foreach (var chat in _restartChats.Values) known[chat.Id] = chat;
        foreach (var list in _projectChatLists.Values)
            foreach (var chat in list) known[chat.Id] = chat;
        foreach (var chat in _recentChats) known[chat.Id] = chat;
        foreach (var chat in _chats) known[chat.Id] = chat;
        return known.Values.ToList();
    }

    private RestartRecovery? GetRestartRecovery()
    {
        if (UnfinishedStatistics.FindRestart(GetKnownChats(), RunState.Runs.Values.ToArray(), _restartSeenAt) is not { } recovery)
            return null;
        var projects = recovery.Tasks.Select(task => task.ProjectId).ToHashSet();
        return recovery with
        {
            ProjectNames = _projects.Where(project => projects.Contains(project.Id)).ToDictionary(project => project.Id, project => project.Name),
            AutoResumeAt = _autoResumeFor == recovery.RecoveredAt ? _autoResumeAt : null,
            Progress = _restartResumeProgress
        };
    }

    /// <summary>
    /// The pill above the composer: shown while the restart group holds work other than the open
    /// branch's. The open branch says so itself, in its turn status and composer line.
    /// </summary>
    private RestartRecovery? GetRestartPill()
    {
        if (_selectedProject is null || GetRestartRecovery() is not { } recovery) return null;
        var openChat = _selectedChat?.Id;
        var openBranch = _selectedChat is null ? (Guid?)null : GetSelectedRunBranchId();
        return recovery.Tasks.Any(task => task.ChatId != openChat || task.BranchId != openBranch) ? recovery : null;
    }

    private static string GetRestartPillText(RestartRecovery recovery, Guid? openChat, Guid? openBranch)
    {
        if (recovery.Progress is { } progress) return $"Resuming {progress.Done + 1} of {progress.Total}…";
        if (recovery.AutoResumeAt is { } at)
        {
            var seconds = Math.Max(0, (int)Math.Ceiling((at - DateTimeOffset.UtcNow).TotalSeconds));
            var count = recovery.Resumable.Count;
            return $"Resuming {count} {(count == 1 ? "task" : "tasks")} the restart stopped in {seconds} s";
        }
        var others = recovery.Tasks.Count(task => task.ChatId != openChat || task.BranchId != openBranch);
        var tasks = $"{others}{(others < recovery.Tasks.Count ? " more" : null)} {(others == 1 ? "task" : "tasks")}";
        return recovery.Crashed ? $"{tasks} stopped by a crash" : $"{tasks} stopped by the restart";
    }

    /// <summary>
    /// Runs after every change of the run snapshots and once the page is set up: reads the chat
    /// lists the group's rows still need, and applies the After a restart setting to a start this
    /// device has not dealt with yet.
    /// </summary>
    private async Task UpdateRestartRecoveryAsync()
    {
        ReadMissingRestartChats();
        if (GetRestartRecovery() is not { } recovery)
        {
            _autoResumeAt = null;
            return;
        }
        switch (_afterRestart)
        {
            case RestartRecoveryMode.LeaveStopped:
                await MarkRestartSeenAsync(recovery.RecoveredAt);
                break;
            case RestartRecoveryMode.ResumeAutomatically when _autoResumeFor != recovery.RecoveredAt
                && _autoResumeCancelledFor != recovery.RecoveredAt && recovery.Resumable.Count > 0:
                _autoResumeFor = recovery.RecoveredAt;
                _autoResumeAt = DateTimeOffset.UtcNow.AddSeconds(RestartRecoveryTiming.CountdownSeconds);
                break;
        }
    }

    // The group is across every project, so a run may belong to a chat no list on the page holds yet.
    private void ReadMissingRestartChats()
    {
        var known = GetKnownChats().Select(chat => chat.Id).ToHashSet();
        var missing = RunState.Runs.Values
            .Where(run => run is { Status: ChatRunStatus.Interrupted, Interruption: not null }
                && run.Interruption.RecoveredAt > (_restartSeenAt ?? DateTimeOffset.MinValue) && !known.Contains(run.ChatId))
            .Select(run => run.ProjectId).Distinct().Where(_restartProjectsRead.Add).ToArray();
        foreach (var projectId in missing) _ = ReadRestartChatsAsync(projectId);
    }

    private async Task ReadRestartChatsAsync(Guid projectId)
    {
        try
        {
            foreach (var chat in await ChatHistoryApi.ListAsync(projectId, CancellationToken.None)) _restartChats[chat.Id] = chat;
            await InvokeAsync(StateHasChanged);
        }
        catch (HttpRequestException)
        {
            // The rows of that project stay out of the group; its chats show their state themselves.
        }
    }

    // Called by the page's one-second ticker while a countdown runs.
    private async Task TickAutoResumeAsync()
    {
        if (_autoResumeAt is not { } at) return;
        if (DateTimeOffset.UtcNow < at)
        {
            StateHasChanged();
            return;
        }
        _autoResumeAt = null;
        await ResumeRestartAsync();
    }

    /// <summary>
    /// Resumes the group one run after another, each in its own project. A run whose tool call never
    /// reported back is left for the person; once nothing else is left, the start counts as dealt with.
    /// </summary>
    private async Task ResumeRestartAsync()
    {
        _autoResumeAt = null;
        if (_restartResumeProgress is not null || GetRestartRecovery() is not { } recovery) return;
        var tasks = recovery.Resumable;
        if (tasks.Count > 0)
        {
            _restartResumeProgress = (0, tasks.Count);
            try
            {
                for (var index = 0; index < tasks.Count; index++)
                {
                    StateHasChanged();
                    var task = tasks[index];
                    await RunQueueCommandAsync(() => ChatRunsApi.ResumeAsync(task.ProjectId, task.ChatId, task.BranchId,
                        Guid.CreateVersion7(), CancellationToken.None));
                    _restartResumeProgress = (index + 1, tasks.Count);
                }
            }
            finally
            {
                _restartResumeProgress = null;
            }
        }
        if (recovery.Tasks.Count == tasks.Count) await MarkRestartSeenAsync(recovery.RecoveredAt);
        StateHasChanged();
    }

    private void CancelAutoResume()
    {
        _autoResumeCancelledFor = _autoResumeFor;
        _autoResumeAt = null;
    }

    private async Task LeaveRestartStoppedAsync()
    {
        if (GetRestartRecovery() is { } recovery) await MarkRestartSeenAsync(recovery.RecoveredAt);
    }

    private async Task MarkRestartSeenAsync(DateTimeOffset recoveredAt)
    {
        _autoResumeAt = null;
        if (_restartSeenAt >= recoveredAt) return;
        _restartSeenAt = recoveredAt;
        await ClientSettings.UpdateAsync(current => current with { RestartSeenAt = recoveredAt });
    }

    /// <summary>Opens the widget column on Unfinished work, shown and unfolded for now even if the person hid or folded it.</summary>
    private async Task ShowRestartWorkAsync()
    {
        _guideExpandedWidgetId = ChatWidgetCatalog.ChatUnfinished;
        // Scrolled to once the column has rendered it; see OnAfterRenderAsync.
        _revealRestartWork = true;
        if (!_chatWidgetsOpen) await SetChatWidgetsOpen(true);
        StateHasChanged();
    }
}
