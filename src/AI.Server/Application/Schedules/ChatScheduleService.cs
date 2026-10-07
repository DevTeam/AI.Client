namespace AI.Application.Schedules;

using AI.Application.Chats;
using AI.Application.Projects;
using AI.Contracts.Chats;
using AI.Contracts.Schedules;

/// <summary>
/// Everything a person or a model does to a schedule: set it up (turning a conversation into a
/// scheduled chat), change it, pause it, remove it, run it now, and — from a run branch — report
/// how the run went. The HTTP endpoints and the <c>app_schedule</c> tool are both thin over this.
/// </summary>
public interface IChatScheduleService
{
    Task<ChatScheduleView?> GetAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the schedule of a conversation or a scheduled chat. A conversation becomes scheduled
    /// with its history intact; an empty chat gets the task as its first message, so the chat has
    /// an instruction to fork runs from. Null when the chat does not exist.
    /// </summary>
    Task<ChatScheduleView?> SetAsync(Guid projectId, Guid chatId, SetChatScheduleRequest request, CancellationToken cancellationToken);

    Task<ChatScheduleView?> PauseAsync(Guid projectId, Guid chatId, bool paused, long? revision, CancellationToken cancellationToken);

    /// <summary>Turns the chat back into a conversation. Runs already going keep going.</summary>
    Task<ChatScheduleView?> RemoveAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);

    /// <summary>Asks the dispatcher for a run now, outside the recurrence.</summary>
    Task<ChatScheduleView?> RunNowAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);

    /// <summary>
    /// Records the outcome a run reports about itself. <paramref name="branchId"/> is the reporting
    /// branch: the run branch or a branch forked from it.
    /// </summary>
    Task<ScheduleRunRecord> ReportRunAsync(Guid projectId, Guid chatId, Guid branchId, bool succeeded, string? summary,
        bool? retry, CancellationToken cancellationToken);
}

/// <summary>The schedule changed since the caller read it.</summary>
public sealed class ScheduleConflictException(ChatScheduleView current)
    : InvalidOperationException($"The schedule changed; its revision is now {current.Schedule?.Revision}. Re-read it and decide whether to repeat the change.")
{
    public ChatScheduleView Current { get; } = current;
}

public sealed class ChatScheduleService(
    IChatScheduleStore store,
    IChatService chats,
    IScheduleCalendar calendar,
    IScheduleDescriptions descriptions,
    IClock clock,
    Func<IChatScheduler> scheduler) : IChatScheduleService
{
    private const string Conversation = "conversation";

    public async Task<ChatScheduleView?> GetAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken) =>
        await store.ReadAsync(projectId, chatId, cancellationToken) is { } stored ? View(projectId, chatId, stored) : null;

    public async Task<ChatScheduleView?> SetAsync(Guid projectId, Guid chatId, SetChatScheduleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = Normalize(request.Settings);
        if (calendar.Validate(settings) is { } invalid) throw new ArgumentException(invalid);
        var now = clock.UtcNow;
        var next = calendar.NextAfter(settings.Recurrence, settings.TimeZone, now)
            ?? throw new ArgumentException($"'{descriptions.Describe(settings.Recurrence)}' has no occurrence after now. Pick a later date or time.");
        var stored = await store.UpdateAsync(projectId, chatId, current =>
        {
            RequireSchedulable(current);
            if (current.Schedule is { } existing)
            {
                RequireRevision(projectId, chatId, current, request.Revision);
                return existing with
                {
                    Settings = settings,
                    Paused = request.Paused ?? existing.Paused,
                    Revision = existing.Revision + 1,
                    NextRunAt = next,
                    // A changed retry rule applies to the next failure, not to one already waiting.
                    RetryAt = settings.Retry is null ? null : existing.RetryAt,
                    // A person who changes the guide's demo makes it their own schedule.
                    Demo = false
                };
            }
            return new ChatSchedule(settings, request.Paused ?? false, NextRunAt: next, Runs: []);
        }, cancellationToken);
        if (stored is null) return null;
        await WriteInstructionAsync(projectId, chatId, settings.Task, cancellationToken);
        scheduler().Nudge();
        return View(projectId, chatId, stored);
    }

    public async Task<ChatScheduleView?> PauseAsync(Guid projectId, Guid chatId, bool paused, long? revision,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var stored = await store.UpdateAsync(projectId, chatId, current =>
        {
            var existing = Scheduled(current);
            RequireRevision(projectId, chatId, current, revision);
            if (existing.Paused == paused) return null;
            // Occurrences that passed while paused are not caught up: the next one is after now.
            return existing with
            {
                Paused = paused,
                Revision = existing.Revision + 1,
                NextRunAt = paused ? existing.NextRunAt
                    : calendar.NextAfter(existing.Settings.Recurrence, existing.Settings.TimeZone, now),
                RetryAt = paused ? null : existing.RetryAt,
                Demo = false
            };
        }, cancellationToken);
        if (stored is null) return null;
        scheduler().Nudge();
        return View(projectId, chatId, stored);
    }

    public async Task<ChatScheduleView?> RemoveAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        var current = await store.ReadAsync(projectId, chatId, cancellationToken);
        if (current is null) return null;
        Scheduled(current);
        var stored = await store.RemoveAsync(projectId, chatId, cancellationToken);
        return stored is null ? null : View(projectId, chatId, stored);
    }

    public async Task<ChatScheduleView?> RunNowAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var stored = await store.UpdateAsync(projectId, chatId, current =>
        {
            var existing = Scheduled(current);
            if (existing.ActiveRun is { } active)
                throw new InvalidOperationException($"Run #{active.Number} is still going; one run goes at a time.");
            return existing with { RunRequestedAt = now, Demo = false };
        }, cancellationToken);
        if (stored is null) return null;
        scheduler().Nudge();
        return View(projectId, chatId, stored);
    }

    public async Task<ScheduleRunRecord> ReportRunAsync(Guid projectId, Guid chatId, Guid branchId, bool succeeded,
        string? summary, bool? retry, CancellationToken cancellationToken)
    {
        if (summary is { Length: > 1000 }) throw new ArgumentException("'summary' is longer than 1000 characters.");
        var chat = await chats.GetTranscriptAsync(projectId, chatId, cancellationToken)
            ?? throw new InvalidOperationException("Chat not found.");
        var lineage = Lineage(chat, branchId);
        ScheduleRunRecord? reported = null;
        await store.UpdateAsync(projectId, chatId, current =>
        {
            var existing = Scheduled(current);
            var run = existing.Runs?.LastOrDefault(item => item.BranchId is { } id && lineage.Contains(id)
                    && item.Status is ScheduleRunStatus.Running or ScheduleRunStatus.Blocked)
                ?? throw new InvalidOperationException("This branch is not a scheduled run that is still going. Report from the run branch.");
            reported = run with
            {
                Reported = succeeded ? ScheduleRunStatus.Succeeded : ScheduleRunStatus.Failed,
                Summary = string.IsNullOrWhiteSpace(summary) ? run.Summary : summary.Trim(),
                RetryRequested = succeeded ? null : retry
            };
            return existing with { Runs = existing.Runs!.Select(item => item.Id == run.Id ? reported : item).ToArray() };
        }, cancellationToken);
        scheduler().Nudge();
        return reported ?? throw new InvalidOperationException("Chat not found.");
    }

    private ChatScheduleView View(Guid projectId, Guid chatId, StoredSchedule stored)
    {
        var now = clock.UtcNow;
        var schedule = stored.Schedule;
        var upcoming = schedule is { Paused: false, NextRunAt: { } next }
            ? new[] { next }.Concat(calendar.Upcoming(schedule.Settings.Recurrence, schedule.Settings.TimeZone, next, 2)).ToArray()
            : [];
        return new ChatScheduleView(projectId, chatId, stored.Kind, schedule,
            schedule is null ? null : descriptions.Describe(schedule.Settings.Recurrence), now,
            calendar.ToLocal(now, calendar.LocalTimeZoneId).ToString("yyyy-MM-dd HH:mm dddd", System.Globalization.CultureInfo.InvariantCulture),
            calendar.LocalTimeZoneId, upcoming);
    }

    private ChatScheduleSettings Normalize(ChatScheduleSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings with
        {
            Task = settings.Task?.Trim() ?? string.Empty,
            SuccessCriteria = string.IsNullOrWhiteSpace(settings.SuccessCriteria) ? null : settings.SuccessCriteria.Trim(),
            TimeZone = string.IsNullOrWhiteSpace(settings.TimeZone) ? calendar.LocalTimeZoneId : calendar.CanonicalTimeZoneId(settings.TimeZone),
            Retry = settings.Retry is { } retry
                ? retry with { Condition = string.IsNullOrWhiteSpace(retry.Condition) ? null : retry.Condition.Trim() } : null
        };
    }

    /// <summary>
    /// An empty chat is hidden from the chat list and has nothing to fork a run from, so the task
    /// becomes its first message — the instruction the chat was created for.
    /// </summary>
    private async Task WriteInstructionAsync(Guid projectId, Guid chatId, string task, CancellationToken cancellationToken)
    {
        var chat = await chats.GetTranscriptAsync(projectId, chatId, cancellationToken);
        if (chat is null || chat.Branches?.SingleOrDefault(branch => branch.Id == chatId)?.HeadMessageId is not null) return;
        await chats.AppendMessageAsync(projectId, chatId,
            new AppendChatMessageRequest(Guid.CreateVersion7(), null, "User", task, chat.Revision), cancellationToken);
    }

    private static HashSet<Guid> Lineage(ChatDetails chat, Guid branchId)
    {
        var branches = (chat.Branches ?? []).ToDictionary(branch => branch.Id);
        var lineage = new HashSet<Guid>();
        Guid? cursor = branchId;
        while (cursor is { } id && lineage.Add(id))
            cursor = branches.TryGetValue(id, out var branch) ? branch.ParentBranchId : null;
        return lineage;
    }

    private static void RequireSchedulable(StoredSchedule current)
    {
        if (current.Kind is not (Conversation or ChatSchedule.Kind))
            throw new InvalidOperationException($"A '{current.Kind}' chat cannot be scheduled; only a conversation can.");
    }

    private static ChatSchedule Scheduled(StoredSchedule current) => current.Schedule
        ?? throw new InvalidOperationException("This chat has no schedule. Set one first.");

    private void RequireRevision(Guid projectId, Guid chatId, StoredSchedule current, long? revision)
    {
        if (revision is { } expected && current.Schedule is { } schedule && schedule.Revision != expected)
            throw new ScheduleConflictException(View(projectId, chatId, current));
    }
}
