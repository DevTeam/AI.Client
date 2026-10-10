namespace AI.Application.Schedules;

using AI.Application.Chats;
using AI.Application.Notifications;
using AI.Application.Runs;
using AI.Contracts.Chats;
using AI.Contracts.Resources;
using AI.Contracts.Runs;
using AI.Contracts.Schedules;

/// <summary>
/// What the dispatcher does for one scheduled chat at one moment: watch the run that is going,
/// finish it with its outcome, delete run branches and the chat when their rules say so, and start
/// the next run — a fork from the end of the chat's main branch, told to follow the
/// <c>chat-schedule-run</c> skill.
/// </summary>
public interface IScheduledChatPass
{
    /// <summary>
    /// When the chat next needs the dispatcher, or null when nothing is pending. A run that is going
    /// is looked at again <see cref="ScheduledChatPass.Watch"/> after it was last looked at
    /// (<paramref name="watchedAt"/>), or at once when it never was.
    /// </summary>
    DateTimeOffset? DueAt(ChatSchedule schedule, DateTimeOffset now, DateTimeOffset? watchedAt);

    Task ProcessAsync(Guid projectId, Guid chatId, Guid ownerBranchId, DateTimeOffset now, CancellationToken cancellationToken);
    Task ProcessAsync(Guid projectId, Guid chatId, DateTimeOffset now, CancellationToken cancellationToken) =>
        ProcessAsync(projectId, chatId, chatId, now, cancellationToken);
}

public sealed class ScheduledChatPass(
    IChatScheduleStore store,
    IChatService chats,
    IChatRunDispatcher dispatcher,
    IScheduleCalendar calendar,
    IScheduleDescriptions descriptions,
    IAppDataChangeSignal changes) : IScheduledChatPass
{
    public const string RunSkillId = "chat-schedule-run";

    /// <summary>How often a run that is going is looked at.</summary>
    internal static readonly TimeSpan Watch = TimeSpan.FromSeconds(5);

    /// <summary>
    /// An occurrence found later than this was missed — the application was closed — and is skipped
    /// rather than run late.
    /// </summary>
    internal static readonly TimeSpan MissedAfter = TimeSpan.FromMinutes(2);

    /// <summary>How long a fork may take to appear before its run counts as lost.</summary>
    private static readonly TimeSpan StartGrace = TimeSpan.FromMinutes(1);

    private const int KeptRuns = 50;

    public static readonly ScheduleBranchRetention DefaultRetention = new(
        new ScheduleRetentionRule(ScheduleRetentionAction.Delete),
        new ScheduleRetentionRule(ScheduleRetentionAction.Keep),
        new ScheduleRetentionRule(ScheduleRetentionAction.Keep));

    public DateTimeOffset? DueAt(ChatSchedule schedule, DateTimeOffset now, DateTimeOffset? watchedAt)
    {
        // The guide's demo only shows a schedule; nothing about it is ever due.
        if (schedule.Demo) return null;
        var candidates = new List<DateTimeOffset?>
        {
            schedule.ActiveRun is null ? null : watchedAt is { } watched ? watched + Watch : now,
            schedule.RunRequestedAt,
            schedule.Paused ? null : schedule.RetryAt,
            schedule.Paused ? null : schedule.NextRunAt,
            schedule.Settings.Deletion?.At,
            ChatDeletionAt(schedule)
        };
        candidates.AddRange((schedule.Runs ?? [])
            .Where(run => run is { BranchDeleteAt: not null, BranchDeleted: false, BranchId: not null })
            .Select(run => run.BranchDeleteAt));
        return candidates.OfType<DateTimeOffset>().Select(at => (DateTimeOffset?)at).Min();
    }

    public async Task ProcessAsync(Guid projectId, Guid chatId, Guid ownerBranchId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Whatever this pass changes — a run started or finished, a branch or the chat deleted — the
        // open windows re-read; nobody there asked for it.
        try
        {
            await ProcessCoreAsync(projectId, chatId, ownerBranchId, now, cancellationToken);
        }
        finally
        {
            changes.Notify();
        }
    }

    private async Task ProcessCoreAsync(Guid projectId, Guid chatId, Guid ownerBranchId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var schedule = await ReadAsync(projectId, chatId, ownerBranchId, cancellationToken);
        if (schedule is null or { Demo: true }) return;
        if (schedule.ActiveRun is { } active)
        {
            await WatchAsync(projectId, chatId, ownerBranchId, schedule, active, now, cancellationToken);
            schedule = await ReadAsync(projectId, chatId, ownerBranchId, cancellationToken);
            if (schedule is null) return;
        }

        foreach (var run in (schedule.Runs ?? []).Where(run =>
                     run is { BranchDeleteAt: { } at, BranchDeleted: false, BranchId: not null } && at <= now).ToArray())
            if (await DeleteRunBranchAsync(projectId, chatId, run.BranchId!.Value, cancellationToken))
                await ChangeRunAsync(projectId, chatId, ownerBranchId, run.Id, item => item with { BranchDeleted = true }, cancellationToken);

        schedule = await ReadAsync(projectId, chatId, ownerBranchId, cancellationToken);
        if (schedule is null) return;
        if (schedule.ActiveRun is null && (schedule.Settings.Deletion?.At <= now || ChatDeletionAt(schedule) <= now))
        {
            await DeleteChatAsync(projectId, chatId, cancellationToken);
            return;
        }

        await StartDueAsync(projectId, chatId, ownerBranchId, schedule, now, cancellationToken);
    }

    private async Task WatchAsync(Guid projectId, Guid chatId, Guid ownerBranchId, ChatSchedule schedule, ScheduleRunRecord run,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var branchId = run.BranchId!.Value;
        var snapshot = (await dispatcher.GetSnapshotAsync(cancellationToken))
            .SingleOrDefault(item => item.ChatId == chatId && item.BranchId == branchId);
        var chat = await chats.GetTranscriptAsync(projectId, chatId, cancellationToken);
        if (chat is null) return;
        var exists = chat.Branches?.Any(branch => branch.Id == branchId) == true;
        // An interrupted or paused run keeps its command until a person resumes or stops it, so it
        // waits for them exactly as a pending approval does.
        var waiting = snapshot is { PendingApproval: not null } or { PendingPrompt: not null }
            or { Status: ChatRunStatus.Interrupted or ChatRunStatus.Paused, Queue.Count: > 0 };
        var busy = snapshot is { Status: ChatRunStatus.Generating } or { Wait: not null }
            or { Status: ChatRunStatus.Idle or ChatRunStatus.Completed, Queue.Count: > 0 };

        if (waiting)
        {
            var blockedAt = run.BlockedAt ?? now;
            if (run.Status != ScheduleRunStatus.Blocked)
                await ChangeRunAsync(projectId, chatId, ownerBranchId, run.Id,
                    item => item with { Status = ScheduleRunStatus.Blocked, BlockedAt = blockedAt }, cancellationToken);
            var rule = Retention(schedule).Blocked;
            if (rule.Action == ScheduleRetentionAction.Delete && blockedAt.AddMinutes(rule.DelayMinutes) <= now
                && await DeleteRunBranchAsync(projectId, chatId, branchId, cancellationToken))
                await FinishAsync(projectId, chatId, ownerBranchId, schedule, run, ScheduleRunStatus.Blocked,
                    "It waited for a person until the rule for blocked runs deleted its branch.", true, now, cancellationToken);
            return;
        }
        if (run.Status == ScheduleRunStatus.Blocked)
            await ChangeRunAsync(projectId, chatId, ownerBranchId, run.Id,
                item => item with { Status = ScheduleRunStatus.Running, BlockedAt = null }, cancellationToken);
        if (busy) return;
        if (!exists)
        {
            if (now - run.StartedAt < StartGrace) return;
            await FinishAsync(projectId, chatId, ownerBranchId, schedule, run, run.Reported ?? ScheduleRunStatus.Failed,
                run.Summary ?? "The run branch was deleted before the run reported an outcome.", true, now, cancellationToken);
            return;
        }
        if (snapshot is null && now - run.StartedAt < StartGrace) return;
        var status = snapshot?.Status ?? ChatRunStatus.Completed;
        var outcome = status is ChatRunStatus.Completed or ChatRunStatus.Idle
            ? run.Reported ?? ScheduleRunStatus.Failed
            : run.Reported == ScheduleRunStatus.Succeeded ? ScheduleRunStatus.Succeeded : ScheduleRunStatus.Failed;
        var summary = run.Summary ?? (status is ChatRunStatus.Completed or ChatRunStatus.Idle
            ? "The run ended without reporting an outcome."
            : snapshot?.Error ?? $"The run stopped ({status.ToString().ToLowerInvariant()}).");
        await FinishAsync(projectId, chatId, ownerBranchId, schedule, run, outcome, summary, false, now, cancellationToken);
    }

    private async Task FinishAsync(Guid projectId, Guid chatId, Guid ownerBranchId, ChatSchedule schedule, ScheduleRunRecord run,
        ScheduleRunStatus outcome, string? summary, bool branchDeleted, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var retention = Retention(schedule);
        var rule = outcome switch
        {
            ScheduleRunStatus.Succeeded => retention.Succeeded,
            ScheduleRunStatus.Blocked => retention.Blocked,
            _ => retention.Failed
        };
        var deleteAt = !branchDeleted && rule.Action == ScheduleRetentionAction.Delete ? now.AddMinutes(rule.DelayMinutes) : (DateTimeOffset?)null;
        var retry = outcome == ScheduleRunStatus.Failed && schedule.Settings.Retry is { } policy
            && run.RetryRequested != false && run.Attempt <= policy.MaxAttempts && !run.Manual
            ? now.AddMinutes(policy.DelayMinutes) : (DateTimeOffset?)null;
        await store.UpdateAsync(projectId, chatId, ownerBranchId, current => current.Schedule is not { } latest ? null : latest with
        {
            Runs = (latest.Runs ?? []).Select(item => item.Id != run.Id ? item : item with
            {
                Status = outcome,
                FinishedAt = now,
                Summary = summary ?? item.Summary,
                BranchDeleteAt = deleteAt,
                BranchDeleted = branchDeleted
            }).ToArray(),
            RetryAt = retry ?? latest.RetryAt
        }, cancellationToken);
        if (!branchDeleted && run.BranchId is { } branchId)
            await chats.RenameBranchAsync(projectId, chatId, branchId, new RenameChatBranchRequest(
                descriptions.RunTitle(run, calendar.ToLocal(run.StartedAt, schedule.Settings.TimeZone),
                    schedule.Settings.Retry?.MaxAttempts ?? 0, outcome), 0), cancellationToken);
    }

    private async Task StartDueAsync(Guid projectId, Guid chatId, Guid ownerBranchId, ChatSchedule schedule, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var settings = schedule.Settings;
        if (schedule.ActiveRun is { } active)
        {
            // One run at a time: an occurrence that comes while one is still going is skipped.
            if (!schedule.Paused && schedule.NextRunAt is { } due && due <= now)
                await AppendAsync(projectId, chatId, ownerBranchId, new ScheduleRunRecord(Guid.CreateVersion7(), null, 0, due, now, 1,
                        ScheduleRunStatus.Skipped, FinishedAt: now, Summary: $"Run #{active.Number} was still going."),
                    latest => latest with { NextRunAt = Following(latest, due, now) }, cancellationToken);
            return;
        }

        if (schedule.RunRequestedAt is not null)
        {
            await StartAsync(projectId, chatId, ownerBranchId, schedule, now, 1, true, now,
                latest => latest with { RunRequestedAt = null }, cancellationToken);
            return;
        }
        if (schedule.Paused) return;
        if (schedule.RetryAt is { } retryAt && retryAt <= now
            && schedule.Runs?.LastOrDefault(run => run is { Status: ScheduleRunStatus.Failed, Manual: false }) is { } failed)
        {
            await StartAsync(projectId, chatId, ownerBranchId, schedule, failed.ScheduledFor, failed.Attempt + 1, false, now,
                latest => latest with { RetryAt = null }, cancellationToken);
            return;
        }
        if (schedule.NextRunAt is not { } next || next > now) return;
        if (now - next > MissedAfter)
        {
            var missed = calendar.Upcoming(settings.Recurrence, settings.TimeZone, next.AddTicks(-1), 1000).Count(at => at <= now);
            await AppendAsync(projectId, chatId, ownerBranchId, new ScheduleRunRecord(Guid.CreateVersion7(), null, 0, next, now, 1,
                    ScheduleRunStatus.Missed, FinishedAt: now,
                    Summary: missed > 1 ? $"{missed} occurrences passed while the application was closed." : "The application was closed at that time."),
                latest => latest with { NextRunAt = calendar.NextAfter(settings.Recurrence, settings.TimeZone, now) }, cancellationToken);
            return;
        }
        await StartAsync(projectId, chatId, ownerBranchId, schedule, next, 1, false, now,
            latest => latest with { NextRunAt = Following(latest, next, now), RetryAt = null, Occurrences = latest.Occurrences + 1 },
            cancellationToken);
    }

    private DateTimeOffset? Following(ChatSchedule schedule, DateTimeOffset occurrence, DateTimeOffset now) =>
        calendar.NextAfter(schedule.Settings.Recurrence, schedule.Settings.TimeZone, occurrence > now ? occurrence : now);

    private async Task StartAsync(Guid projectId, Guid chatId, Guid ownerBranchId, ChatSchedule schedule, DateTimeOffset scheduledFor, int attempt,
        bool manual, DateTimeOffset now, Func<ChatSchedule, ChatSchedule> advance, CancellationToken cancellationToken)
    {
        var settings = schedule.Settings;
        var chat = await chats.GetTranscriptAsync(projectId, chatId, cancellationToken);
        if (chat is null) return;
        var messageId = Guid.CreateVersion7();
        var record = new ScheduleRunRecord(Guid.CreateVersion7(), messageId, schedule.RunNumber + 1, scheduledFor, now, attempt,
            ScheduleRunStatus.Running, manual);
        var maxAttempts = settings.Retry?.MaxAttempts ?? 0;
        var local = calendar.ToLocal(now, settings.TimeZone);
        var head = chat.Branches?.SingleOrDefault(branch => branch.Id == ownerBranchId)?.HeadMessageId;
        try
        {
            // A fork from the end of the main branch: the run sees the whole conversation that set
            // the task up, and nothing a previous run did.
            await dispatcher.SubmitAsync(projectId, chatId, new SubmitChatMessageRequest(
                Guid.CreateVersion7(), messageId, Message(schedule, record, local, maxAttempts), ChatSubmitMode.Fork, ownerBranchId,
                head is null ? MessageParentMode.Root : MessageParentMode.BranchHead,
                Resources: [new ChatResource(Guid.CreateVersion7(), ChatResourceKind.Skill, RunSkillId, "Chat schedule run")],
                BranchTitle: descriptions.RunTitle(record, local, maxAttempts)), cancellationToken);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            record = record with
            {
                BranchId = null, Status = ScheduleRunStatus.Failed, FinishedAt = now,
                Summary = $"The run could not start: {error.Message}"
            };
        }
        await AppendAsync(projectId, chatId, ownerBranchId, record, latest => advance(latest) with { RunNumber = record.Number }, cancellationToken);
    }

    private string Message(ChatSchedule schedule, ScheduleRunRecord run, DateTime local, int maxAttempts)
    {
        var settings = schedule.Settings;
        var lines = new List<string>
        {
            $"Scheduled run #{run.Number} · {descriptions.Moment(local)} ({settings.TimeZone})"
                + (run.Manual ? " · started by hand" : maxAttempts > 0 ? $" · attempt {run.Attempt} of {maxAttempts + 1}" : string.Empty),
            "Task: " + settings.Task
        };
        if (settings.SuccessCriteria is { } criteria) lines.Add("Success criteria: " + criteria);
        if (settings.Retry?.Condition is { } condition) lines.Add("Retry when: " + condition);
        lines.Add($"Follow the {RunSkillId} skill: carry out the task, then report the outcome with app_schedule ReportRun.");
        // Paragraphs, so the message reads field by field in the transcript as well as to the model.
        return string.Join("\n\n", lines);
    }

    private async Task AppendAsync(Guid projectId, Guid chatId, Guid ownerBranchId, ScheduleRunRecord record, Func<ChatSchedule, ChatSchedule> change,
        CancellationToken cancellationToken) =>
        await store.UpdateAsync(projectId, chatId, ownerBranchId, current => current.Schedule is not { } latest ? null
            : change(latest) with { Runs = Trim([.. latest.Runs ?? [], record]) }, cancellationToken);

    /// <summary>
    /// Keeps the latest runs. An older one stays while its branch still waits to be deleted, because
    /// the record is the only thing that remembers to.
    /// </summary>
    private static ScheduleRunRecord[] Trim(IReadOnlyList<ScheduleRunRecord> runs)
    {
        var surplus = runs.Count - KeptRuns;
        if (surplus <= 0) return [.. runs];
        var dropped = runs
            .Where(run => run.Status is not (ScheduleRunStatus.Running or ScheduleRunStatus.Blocked)
                && run is not { BranchDeleteAt: not null, BranchDeleted: false })
            .Take(surplus).Select(run => run.Id).ToHashSet();
        return runs.Where(run => !dropped.Contains(run.Id)).ToArray();
    }

    private Task<StoredSchedule?> ChangeRunAsync(Guid projectId, Guid chatId, Guid ownerBranchId, Guid runId, Func<ScheduleRunRecord, ScheduleRunRecord> change,
        CancellationToken cancellationToken) =>
        store.UpdateAsync(projectId, chatId, ownerBranchId, current => current.Schedule is not { } latest ? null : latest with
        {
            Runs = (latest.Runs ?? []).Select(item => item.Id == runId ? change(item) : item).ToArray()
        }, cancellationToken);

    /// <summary>
    /// Deletes a run branch with every branch forked from it, deepest first: deleting a branch hands
    /// its children to its parent, which here would leave them behind on the main branch.
    /// </summary>
    private async Task<bool> DeleteRunBranchAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var chat = await chats.GetTranscriptAsync(projectId, chatId, cancellationToken);
            if (chat?.Branches is not { } branches) return false;
            if (branches.All(branch => branch.Id != branchId)) return true;
            var tree = Subtree(branches, branchId);
            var leaf = tree.First(id => branches.All(branch => branch.ParentBranchId != id));
            try
            {
                await dispatcher.DeleteBranchAsync(projectId, chatId, leaf, chat.Revision, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // The chat is being changed by someone else; the next pass tries again.
                return false;
            }
        }
        return false;
    }

    private static List<Guid> Subtree(IReadOnlyList<ChatBranchView> branches, Guid rootId)
    {
        var result = new List<Guid>();
        var pending = new Queue<Guid>([rootId]);
        while (pending.TryDequeue(out var id))
        {
            result.Add(id);
            foreach (var child in branches.Where(branch => branch.ParentBranchId == id)) pending.Enqueue(child.Id);
        }
        result.Reverse();
        return result;
    }

    private async Task DeleteChatAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var chat = await chats.GetTranscriptAsync(projectId, chatId, cancellationToken);
            if (chat is null) return;
            try
            {
                if ((await dispatcher.DeleteChatAsync(projectId, chatId, chat.Revision, cancellationToken)).IsDeleted) return;
            }
            catch (InvalidOperationException)
            {
                return;
            }
        }
    }

    /// <summary>When an "after the last run" deletion rule deletes the chat; null while runs remain.</summary>
    private static DateTimeOffset? ChatDeletionAt(ChatSchedule schedule)
    {
        if (schedule.Settings.Deletion?.AfterLastRunMinutes is not { } minutes) return null;
        if (schedule.NextRunAt is not null || schedule.RetryAt is not null || schedule.RunRequestedAt is not null
            || schedule.ActiveRun is not null) return null;
        var last = (schedule.Runs ?? []).Where(run => run.BranchId is not null).Select(run => run.FinishedAt).Max();
        return last?.AddMinutes(minutes);
    }

    private static ScheduleBranchRetention Retention(ChatSchedule schedule) => schedule.Settings.Retention ?? DefaultRetention;

    private async Task<ChatSchedule?> ReadAsync(Guid projectId, Guid chatId, Guid ownerBranchId, CancellationToken cancellationToken) =>
        (await store.ReadAsync(projectId, chatId, ownerBranchId, cancellationToken))?.Schedule;
}
