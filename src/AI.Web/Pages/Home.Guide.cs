namespace AI.Web.Pages;

using AI.Contracts.Navigation;
using AI.Contracts.Runs;
using AI.Web.Components;
using AI.Web.Widgets;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public partial class Home
{
    private readonly Guid _guideClientId = Guid.CreateVersion7();
    private IJSObjectReference? _guideModule;
    private IJSObjectReference? _guideSubscription;
    private readonly List<AppNavigation> _pendingGuideNavigation = [];
    private AppNavigation? _guideStep;
    private UserPrompt? _guideInvitation;
    private Guid? _guideChatId;
    private Guid? _guideProjectId;
    private string? _guideTopic;
    private string _guideMode = "show";
    private int _guideStepNumber;
    private int _guideStepSeconds = AppNavigation.DefaultTimeoutSeconds;
    // A guide step goes on by itself when its time runs out; an ordinary assistant request still stops.
    private bool _guideStepAutoContinue;
    private DateTimeOffset _guideStepDeadlineAt;
    // The Host stops waiting at ExpiresAt, so the automatic Continue leaves time for its click and reply.
    private static readonly TimeSpan GuideAutoContinueLead = TimeSpan.FromSeconds(2);
    private bool _guideStarting;
    private bool _guidePaused;
    private bool _guideStepBusy;
    private bool _guideTargetUsed;
    private string? _guideNote;
    private string? _guideExpandedWidgetId;
    private bool _guideStopped;
    private bool _guideDisposing;
    private IReadOnlyList<string> _completedGuideTopics = [];
    private CancellationTokenSource? _guideDeadline;
    private bool GuideRunning => _guideStarting || _guideChatId is not null || _guideStep is not null;

    private async Task AttachGuideAsync()
    {
        _guideModule = await JsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/appGuide.js");
        _guideSubscription = await _guideModule.InvokeAsync<IJSObjectReference>("attach", _dotNetReference);
        _completedGuideTopics = (await ClientSettings.GetAsync()).CompletedGuideTopics;
    }

    private async Task StartGuideTopicAsync(AppGuideTopicsView.GuideTopicSelection selection)
    {
        if (GuideRunning) return;
        _guideInvitation = null;
        _guideStarting = true;
        _guideNote = null;
        _guideTopic = selection.Topic;
        _guideMode = selection.Mode;
        _guideStepNumber = 0;
        _guideUnavailableInRow = 0;
        _guideStopped = false;
        _guidePaused = false;
        try
        {
            await ShowGuideWidgetAsync();
            var projectId = _selectedProject?.Id ?? _projects.FirstOrDefault()?.Id;
            if (projectId is null)
            {
                // Before the first project/connection exists, the bootstrap tour needs no model.
                await ShowOfflineTourAsync(needsProject: true);
                return;
            }
            if (!_connections.Any(item => item.Enabled))
            {
                // Every guide tour is run by a model; without one, show where to enable it instead
                // of starting a tour that can only fail.
                await ShowOfflineTourAsync(needsProject: false);
                return;
            }
            var module = _guideModule ??= await JsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/appGuide.js");
            var locale = await module.InvokeAsync<string?>("language");
            var snapshot = await GuideApi.StartAsync(new AppGuideStartRequest(projectId.Value,
                selection.Topic, selection.Mode, _selectedChat?.Id,
                _selectedChat is null ? null : GetSelectedRunBranchId(), UiLocale: locale), CancellationToken.None);
            StoreRun(snapshot);
            if (_guideStopped || _guideDisposing)
            {
                await ChatRunsApi.StopAsync(snapshot.ProjectId, snapshot.ChatId, snapshot.ChatId,
                    Guid.CreateVersion7(), CancellationToken.None);
                return;
            }
            _guideChatId = snapshot.ChatId;
            _guideProjectId = snapshot.ProjectId;
            // The run may already have failed before its chat was known here.
            await CheckGuideStateAsync();
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException)
        {
            _guideNote = "The guide could not start. Check the connection in Settings.";
            _guideTopic = null;
        }
        finally
        {
            _guideStarting = false;
            var pending = _pendingGuideNavigation.ToArray();
            _pendingGuideNavigation.Clear();
            foreach (var navigation in pending) await HandleGuideNavigationAsync(navigation);
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// The guide widget is where a tour shows its progress, its Pause and Stop, and why it ended,
    /// so starting one opens the widget rail with that widget unfolded, wherever it had been put.
    /// </summary>
    private async Task ShowGuideWidgetAsync()
    {
        if (_selectedProject is null) return;
        _chatWidgetsOpen = true;
        _chatWidgets = WidgetLayout.Update(_chatWidgets, ChatWidgetCatalog.AppGuide,
            item => item with { Hidden = false, Collapsed = false });
        await ClientSettings.UpdateAsync(current => current with { ChatWidgetsOpen = true, ChatWidgets = _chatWidgets });
    }

    // The tour shown without a model: every other tour is run by one. It starts with the thing
    // that is missing, a connection, walking through its fields in the order they are filled in,
    // and then, before the first project, where to create one. _bootstrapStep is the 1-based step
    // on screen, 0 when no offline tour is running.
    private int _bootstrapStep;
    private List<AppNavigation> _offlineSteps = [];
    private bool _offlineCompletesBasic;
    // Typing a URL and finding a key takes longer than reading a comment.
    private const int OfflineStepSeconds = 45;

    private Task ShowOfflineTourAsync(bool needsProject)
    {
        var hasConnection = _connections.Count > 0;
        var steps = new List<AppNavigation>
        {
            hasConnection
                ? Offline("settings.connections", "show")
                : Offline("settings.connections.add", "click"),
            Offline("settings.connection.url", "show"),
            Offline("settings.connection.credential", "show"),
            Offline("settings.connection.model", "show"),
            Offline("settings.connection.enabled", "show"),
            Offline("settings.connection.default", "show"),
            Offline("settings.connections.close", "show")
        };
        if (needsProject)
            steps.Add(Offline("projects.create", "show"));
        _offlineSteps = steps;
        _offlineCompletesBasic = needsProject;
        return ShowOfflineStepAsync(1);
    }

    private AppNavigation Offline(string target, string action) =>
        new(Guid.Empty, Target: target, Action: action, Comment: GuideTargets.Find(target)?.Hint, WaitForContinue: true);

    private Task ShowOfflineStepAsync(int number)
    {
        _bootstrapStep = number;
        return HandleGuideNavigationAsync(_offlineSteps[number - 1] with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(OfflineStepSeconds) });
    }

    private async Task HandleGuideNavigationAsync(AppNavigation target)
    {
        if (target.ExpiresAt <= DateTimeOffset.UtcNow) return;
        // A fast first tool call can arrive over SSE before the start response identifies its chat.
        if (_guideStarting && target.RequestId != Guid.Empty && target.SourceChatId != _guideChatId)
        {
            _pendingGuideNavigation.Add(target);
            return;
        }
        if (target.RequestId != Guid.Empty)
        {
            if (target.SourceChatId != _guideChatId && RunState.Runs.Values.Any(run => run.ChatId == target.SourceChatId && run.InteractionSurface == "guide")) return;
            // A hidden guide belongs to the window that started it. Ordinary chats may offer a move
            // in the focused window, preserving the existing navigation behavior.
            if (target.SourceChatId != _guideChatId && target.SourceChatId != _selectedChat?.Id && !_applicationFocused) return;
            if (!await GuideApi.ClaimAsync(target.RequestId, _guideClientId, CancellationToken.None)) return;
        }
        // The guide's own chats stay hidden even if a step names one: the tour would open its
        // instructions in front of the person.
        if (target.ChatId is { } namedChat && (namedChat == target.SourceChatId || namedChat == _guideChatId
            || RunState.Runs.Values.Any(run => run.ChatId == namedChat && run.InteractionSurface == "guide")))
        {
            if (target.RequestId != Guid.Empty)
                await GuideApi.CompleteAsync(target.RequestId, new AppNavigationDecision(_guideClientId, "unavailable",
                    "That is a hidden service chat and is never shown."), CancellationToken.None);
            return;
        }
        if (target.Action == "targets")
        {
            var catalogModule = _guideModule ??= await JsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/appGuide.js");
            var available = await catalogModule.InvokeAsync<AppNavigationTarget[]>("describeTargets", target, GuideTargets.All);
            await GuideApi.CompleteAsync(target.RequestId, new AppNavigationDecision(_guideClientId, "applied", Targets: available), CancellationToken.None);
            return;
        }
        if (!target.WaitForContinue && target.Action is not ("show" or "hover"))
        {
            var cueModule = await NavigationCueModuleAsync();
            if (target.SourceChatId != _selectedChat?.Id || await cueModule.InvokeAsync<bool>("isUserBusy", NavigationQuietMs))
                target = target with { WaitForContinue = true, Comment = target.Comment ?? "The assistant would like to open this item. Continue when you are ready." };
        }
        if (_guideStep is not null) await FinishGuideStepAsync("stopped");
        target = target with { ExpiresAt = target.ExpiresAt ?? DateTimeOffset.UtcNow.AddSeconds(AppNavigation.DefaultTimeoutSeconds) };
        _guideStep = target;
        var expiresAt = target.ExpiresAt!.Value;
        _guideStepAutoContinue = target.RequestId == Guid.Empty ? _bootstrapStep > 0
            : _guideChatId is not null && target.SourceChatId == _guideChatId;
        var window = expiresAt - DateTimeOffset.UtcNow;
        _guideStepDeadlineAt = _guideStepAutoContinue
            ? expiresAt - TimeSpan.FromTicks(Math.Min(GuideAutoContinueLead.Ticks, Math.Max(0, window.Ticks / 5)))
            : expiresAt;
        _guideStepSeconds = Math.Max(1, (int)Math.Ceiling((_guideStepDeadlineAt - DateTimeOffset.UtcNow).TotalSeconds));
        _guideDeadline = new CancellationTokenSource();
        _ = ExpireGuideStepAsync(target, _guideStepAutoContinue ? _guideStepDeadlineAt : null, _guideDeadline.Token);
        _guideTargetUsed = false;
        _guideStepNumber++;
        try
        {
            await RevealGuideTargetAsync(target);
            await InvokeAsync(StateHasChanged);
            var module = _guideModule ??= await JsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/appGuide.js");
            // Wait for Blazor's render without assuming that a timer means the target was painted.
            if (!await module.InvokeAsync<bool>("waitForTarget", target))
            {
                await FinishGuideStepAsync("unavailable", "The target is not available in this view.");
                return;
            }
            await module.InvokeAsync<bool>("show", target);
            if (_guideStep != target) return;
            if (!_guidePaused)
            {
                // Every step first points at its control; activation waits for Continue.
                var preview = target with { Action = target.Action == "hover" ? "hover" : "show" };
                var error = await module.InvokeAsync<string?>("perform", preview);
                if (_guideStep != target || _guidePaused) return;
                if (error is not null) { await FinishGuideStepAsync("unavailable", error); return; }
            }
            if (target.WaitForUser) await module.InvokeVoidAsync("watchTarget", target, _dotNetReference);
            if (!target.WaitForContinue && !target.WaitForUser && string.IsNullOrWhiteSpace(target.Comment))
            {
                await ContinueGuideStepAsync();
                return;
            }
            await InvokeAsync(StateHasChanged);
            await module.InvokeVoidAsync("position", target);
        }
        catch (Exception error) when (error is HttpRequestException or JSException or InvalidOperationException)
        {
            await FinishGuideStepAsync("unavailable", "The target could not be shown.");
        }
    }

    private async Task RevealGuideTargetAsync(AppNavigation target)
    {
        _guideExpandedWidgetId = _chatWidgets.FirstOrDefault(widget => (target.Target == "widgets." + widget.Id
                || target.Target?.StartsWith("widgets." + widget.Id + ".", StringComparison.Ordinal) == true))?.Id;
        var definition = GuideTargets.Find(target.Target ?? "project");
        if (definition?.Section is null)
        {
            if (_permissionsScope is not null) ClosePermissions();
            if (_globalSection is not null) await RequestCloseGlobalSettingsAsync();
            if (_isProjectSettingsOpen) await RequestCloseProjectSettingsAsync();
            if (_globalSection is not null || _isProjectSettingsOpen)
                throw new InvalidOperationException("Close the settings panel before continuing the guide.");
        }
        if (target.ProjectId != Guid.Empty && target.ProjectId != _selectedProject?.Id && definition?.Section is null && target.Target != "project")
            await NavigateToTargetAsync(new WorkspaceTarget(target.ProjectId, null, null, null, false));
        // A chat the guide has just set up (its demo chat) may not have reached the sidebar yet.
        if (target.Target is "chat" or "branch" && target.ChatId is { } listedChat && target.ProjectId == _selectedProject?.Id
            && _chats.All(item => item.Id != listedChat))
            await LoadChatsAsync(target.ProjectId);
        if (definition?.Section == "Search")
        {
            if (!_isSearchOpen) await ToggleSearchPanelAsync();
        }
        else if (definition?.Section == "Widgets")
        {
            if (!await CloseDrawersExceptAsync(Drawer.Widgets))
                throw new InvalidOperationException("Close the current drawer before showing widgets.");
            if (target.ProjectId != _selectedProject?.Id)
                await NavigateToTargetAsync(new WorkspaceTarget(target.ProjectId, target.ChatId, target.BranchId, null, false));
            if (!_chatWidgetsOpen) await ToggleChatWidgetsAsync();
        }
        else if (definition?.Section is "ProjectPermissions" or "ChatPermissions")
        {
            if (target.ProjectId != _selectedProject?.Id)
                await NavigateToTargetAsync(new WorkspaceTarget(target.ProjectId, null, null, null, false));
            if (definition.Section == "ProjectPermissions") await OpenProjectPermissions();
            else
            {
                if (target.ChatId is not { } permissionsChatId)
                    throw new InvalidOperationException("Choose a visible chat to show its permissions.");
                if (_selectedChat?.Id != permissionsChatId) await SelectChatAsync(permissionsChatId);
                var permissionsChat = _chats.FirstOrDefault(item => item.Id == permissionsChatId)
                    ?? throw new InvalidOperationException("The chat is not available in this project.");
                await OpenChatPermissions(permissionsChat);
            }
        }
        else if (definition?.Section == "ProjectSettings")
        {
            if (target.ProjectId != _selectedProject?.Id)
                await NavigateToTargetAsync(new WorkspaceTarget(target.ProjectId, null, null, null, false));
            if (!_isProjectSettingsOpen) await OpenProjectSettings();
        }
        else if (definition?.Section is { } section && _globalSection != section) await OpenGlobalSection(section);
        if (target.Target == "branch" && target.ChatId is { } chatId)
        {
            // Showing a branch may expand its chat, without selecting that branch.
            if (_selectedChat?.Id != chatId) await SelectChatAsync(chatId);
        }
        else if (target.Target?.StartsWith("chat.", StringComparison.Ordinal) == true
            && target.ChatId is { } controlChatId && _selectedChat?.Id != controlChatId)
            await SelectChatAsync(controlChatId);
        await SyncWorkspaceUrlAsync();
    }

    private Task ContinueGuideStepAsync() => ContinueGuideStepAsync(automatic: false);

    /// <param name="automatic">The step's time ran out: it goes on even if the user has not tried the control.</param>
    private async Task ContinueGuideStepAsync(bool automatic)
    {
        if (_guideStep is not { } target || _guideStepBusy || _guidePaused || target.WaitForUser && !_guideTargetUsed && !automatic) return;
        if (target.ExpiresAt <= DateTimeOffset.UtcNow) { await FinishGuideStepAsync("expired"); return; }
        _guideStepBusy = true;
        try
        {
            var module = _guideModule!;
            if (target.Action is not ("show" or "hover"))
            {
                var domainTarget = target.Target is "project" or "chat" or "branch";
                var error = await module.InvokeAsync<string?>("perform", target, !domainTarget);
                if (_guideStep != target || _guidePaused) return;
                if (error is not null) { await FinishGuideStepAsync("unavailable", error); return; }
                if (domainTarget && target.Action == "click")
                {
                    await NavigateToTargetAsync(new WorkspaceTarget(target.ProjectId, target.ChatId, target.BranchId, null, false));
                    await SyncWorkspaceUrlAsync();
                }
            }
            await FinishGuideStepAsync("applied");
            if (_bootstrapStep > 0 && _bootstrapStep < _offlineSteps.Count) await ShowOfflineStepAsync(_bootstrapStep + 1);
            else if (_bootstrapStep > 0)
            {
                _bootstrapStep = 0;
                _guideNote = "Start the guide again once a model connection is enabled.";
                if (_offlineCompletesBasic) await CompleteGuideTopicAsync("basic");
            }
        }
        finally { _guideStepBusy = false; await InvokeAsync(StateHasChanged); }
    }

    private int _guideUnavailableInRow;
    private const int GuideUnavailableLimit = 3;

    private async Task FinishGuideStepAsync(string outcome, string? error = null)
    {
        if (_guideStep is not { } target) return;
        _guideStep = null;
        _guideDeadline?.Cancel();
        _guideDeadline?.Dispose();
        _guideDeadline = null;
        // The card fades out instead of blinking away before the next step appears.
        if (_guideModule is not null) await _guideModule.InvokeVoidAsync("dismiss");
        if (target.RequestId != Guid.Empty)
            await GuideApi.CompleteAsync(target.RequestId, new AppNavigationDecision(_guideClientId, outcome, error), CancellationToken.None);
        // A control missing from this view is something the model can work around: it is told so
        // and picks a visible one. Only a run of misses means the tour has lost its way.
        _guideUnavailableInRow = outcome == "unavailable" ? _guideUnavailableInRow + 1 : 0;
        var recoverable = outcome == "unavailable" && target.RequestId != Guid.Empty
            && target.SourceChatId == _guideChatId && _guideUnavailableInRow < GuideUnavailableLimit;
        if (outcome != "applied" && !recoverable)
        {
            _guideExpandedWidgetId = null;
            _guideStopped = true;
            _guideNote = error ?? "Guide stopped.";
            if (target.SourceChatId == _guideChatId && _guideChatId is { } chatId && _guideProjectId is { } projectId)
            {
                _guideChatId = null;
                _guideCleanupProjectId = _guideProjectId;
                await ChatRunsApi.StopAsync(projectId, chatId, chatId, Guid.CreateVersion7(), CancellationToken.None);
            }
        }
        if (!_guideDisposing) await InvokeAsync(StateHasChanged);
    }

    private async Task ExpireGuideStepAsync(AppNavigation target, DateTimeOffset? continueAt, CancellationToken token)
    {
        try
        {
            if (continueAt is { } at)
            {
                // A paused guide waits for the user; without Resume the step stops at its expiry below.
                await PressWhenDueAsync(target, at, GuideContinueButton, () => !_guidePaused, token);
                if (_guideStep == target && !_guidePaused) await InvokeAsync(() => ContinueGuideStepAsync(automatic: true));
            }
            var expiresAt = target.ExpiresAt ?? DateTimeOffset.UtcNow.AddSeconds(AppNavigation.DefaultTimeoutSeconds);
            await PressWhenDueAsync(target, expiresAt, GuideStopButton, () => true, token);
            if (_guideStep == target) await InvokeAsync(() => StopGuideAsync("No response. The guide was cancelled."));
        }
        catch (OperationCanceledException) { }
    }

    // The virtual pointer reaches and presses the button the timer is about to apply just before it
    // runs out, so the step does not change under the person with nothing to show why.
    private static readonly TimeSpan GuidePressLead = TimeSpan.FromMilliseconds(1200);
    private const string GuideContinueButton = ".app-guide-step:not(.is-leaving) button[data-app-guide-action=\"continue\"]";
    private const string GuideStopButton = ".app-guide-step:not(.is-leaving) button[data-app-guide-action=\"stop\"]";

    private async Task PressWhenDueAsync(AppNavigation target, DateTimeOffset at, string button, Func<bool> applies, CancellationToken token)
    {
        var wait = at - GuidePressLead - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero) await Task.Delay(wait, token);
        if (_guideStep == target && applies() && _guideModule is { } module)
        {
            try { await InvokeAsync(async () => await module.InvokeAsync<bool>("press", token, button)); }
            catch (JSException) { }
        }
        var remaining = at - DateTimeOffset.UtcNow;
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining, token);
    }

    private async Task StopGuideAsync(string note = "Guide stopped.")
    {
        _guideExpandedWidgetId = null;
        _guideInvitation = null;
        _guideStopped = true;
        _guidePaused = false;
        _bootstrapStep = 0;
        await FinishGuideStepAsync("stopped");
        if (_guideChatId is { } chatId && _guideProjectId is { } projectId)
        {
            _guideChatId = null;
            _guideCleanupProjectId = _guideProjectId;
            await ChatRunsApi.StopAsync(projectId, chatId, chatId, Guid.CreateVersion7(), CancellationToken.None);
        }
        _guideNote = note;
        if (!_guideDisposing) await InvokeAsync(StateHasChanged);
    }

    private Task StopGuideStepAsync() => StopGuideAsync();

    private async Task ToggleGuidePauseAsync()
    {
        _guidePaused = !_guidePaused;
        if (_guidePaused && _guideModule is not null) await _guideModule.InvokeVoidAsync("cancelAnimation");
        else if (_guideStep is { } target && _guideModule is not null)
        {
            await _guideModule.InvokeAsync<bool>("show", target);
            if (target.WaitForUser) await _guideModule.InvokeVoidAsync("watchTarget", target, _dotNetReference);
            await _guideModule.InvokeAsync<string?>("perform", target with { Action = target.Action == "hover" ? "hover" : "show" });
            await _guideModule.InvokeVoidAsync("position", target);
        }
    }

    [JSInvokable] public Task OnGuideEscape() => StopGuideAsync();
    [JSInvokable] public Task OnGuideTargetUsed(Guid requestId)
    {
        if (_guideStep?.RequestId == requestId) _guideTargetUsed = true;
        return InvokeAsync(StateHasChanged);
    }

    private async Task CompleteGuideTopicAsync(string topic)
    {
        var settings = await ClientSettings.UpdateAsync(current => current with
        { CompletedGuideTopics = current.CompletedGuideTopics.Append(topic).Distinct(StringComparer.Ordinal).ToArray() });
        _completedGuideTopics = settings.CompletedGuideTopics;
    }

    // A finished tour's service chat is removed once its run has stopped and its outcome was read.
    private Guid? _guideCleanupProjectId;

    private async Task RemoveFinishedGuideChatsAsync()
    {
        if (_guideCleanupProjectId is not { } projectId || GuideRunning
            || RunState.Runs.Values.Any(run => run.InteractionSurface == "guide" && run.ProjectId == projectId && run.Status == ChatRunStatus.Generating)) return;
        _guideCleanupProjectId = null;
        try { await GuideApi.CleanUpAsync(projectId, CancellationToken.None); }
        catch (HttpRequestException) { }
    }

    private async Task CheckGuideStateAsync()
    {
        // A failed or interrupted run keeps its message queued for a retry, so the guide is over
        // then even with a queue; otherwise Start would wait on it forever.
        if (_guideChatId is { } id && GetRun(id, id) is { } run && run.Status != ChatRunStatus.Generating
            && (run.Queue.Count == 0 || run.Status is ChatRunStatus.Failed or ChatRunStatus.Interrupted))
        {
            _guideExpandedWidgetId = null;
            _guideChatId = null;
            _guideCleanupProjectId = _guideProjectId;
            if (_guideStep is not null) await FinishGuideStepAsync("stopped");
            if (!_guideStopped && _guideStepNumber > 0 && run.Status == ChatRunStatus.Completed && _guideTopic is { } topic)
            {
                await CompleteGuideTopicAsync(topic);
                _guideNote = "Guide completed.";
            }
            else _guideNote = run.Error ?? "Guide stopped.";
            await InvokeAsync(StateHasChanged);
        }
        await RemoveFinishedGuideChatsAsync();
        if (GuideRunning || _guideInvitation is not null || OutsideChatQuestion is not null || _guideModule is null || !_applicationFocused
            || !string.IsNullOrWhiteSpace(_chat.Message) || _globalSection is not null || _isProjectSettingsOpen
            || RunState.Runs.Values.Any(run => run.ShowInMainRuns && (run.Status == ChatRunStatus.Generating
                || run.PendingPrompt is not null || run.PendingApproval is not null))) return;
        var preferences = await ClientSettings.GetAsync();
        _completedGuideTopics = preferences.CompletedGuideTopics;
        if (!preferences.GuideSuggestionsEnabled || preferences.GuideLastOfferedAt > DateTimeOffset.UtcNow.AddDays(-1)
            || GuideTopics.All.All(topic => preferences.CompletedGuideTopics.Contains(topic.Id))) return;
        var minutes = Math.Clamp(preferences.GuideIdleMinutes, 1, 120);
        if (await _guideModule.InvokeAsync<double>("idleSeconds") < minutes * 60) return;
        if (!await _guideModule.InvokeAsync<bool>("tryOffer")) return;
        var next = GuideTopics.All.First(topic => !preferences.CompletedGuideTopics.Contains(topic.Id));
        _guideTopic = next.Id;
        await ClientSettings.UpdateAsync(current => current with { GuideLastOfferedAt = DateTimeOffset.UtcNow });
        _guideInvitation = new UserPrompt(Guid.CreateVersion7(),
            [new UserPromptQuestion("start", $"Would you like a short guide: {next.Title}?", "Application guide",
                [new UserPromptOption("Start guide", next.Description), new UserPromptOption("Not now", null, true)], false, false)],
            30, "overlay", ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(30));
        await InvokeAsync(StateHasChanged);
    }

    private async Task<bool> AnswerGuideInvitationAsync(UserPromptResponse response)
    {
        if (_guideInvitation?.Id != response.PromptId) return false;
        _guideInvitation = null;
        if (response.Outcome == UserPromptOutcome.Answered && response.Answers.Any(answer => answer.Selected.Contains(0)))
            await StartGuideTopicAsync(new(_guideTopic ?? "basic", "show"));
        await InvokeAsync(StateHasChanged);
        return true;
    }

    private ChatRunSnapshot? OutsideChatQuestion => RunState.Runs.Values
        .Where(run => run.PendingPrompt?.Presentation == "overlay" && (run.InteractionSurface != "guide" || run.ChatId == _guideChatId))
        .OrderBy(run => run.PendingPrompt!.ExpiresAt).FirstOrDefault();

    private async Task<bool> AnswerOutsideChatQuestionAsync(ChatRunSnapshot run, UserPromptResponse response)
    {
        var accepted = await ChatRunsApi.AnswerPromptAsync(run.ProjectId, run.ChatId, run.BranchId, response, CancellationToken.None);
        if (accepted && run.InteractionSurface == "guide" && response.Outcome != UserPromptOutcome.Answered) await StopGuideAsync();
        return accepted;
    }

    private async Task DisposeGuideAsync()
    {
        _guideDisposing = true;
        try
        {
            if (_guideChatId is not null || _guideStep is not null) await StopGuideAsync();
            if (_guideSubscription is not null) { await _guideSubscription.InvokeVoidAsync("dispose"); await _guideSubscription.DisposeAsync(); }
            if (_guideModule is not null) await _guideModule.DisposeAsync();
        }
        catch (Exception error) when (error is JSDisconnectedException or HttpRequestException or ObjectDisposedException) { }
        _guideDeadline?.Cancel();
        _guideDeadline?.Dispose();
    }
}
