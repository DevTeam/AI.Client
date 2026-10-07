namespace AI.Application.Schedules;

using System.Globalization;
using AI.Application.Chats;
using AI.Application.Projects;
using AI.Contracts.Chats;
using AI.Contracts.Schedules;

/// <summary>
/// The demo chat the application guide sets up to show scheduled chats, written without a model: a
/// request to post an exchange rate every weekday, the answer that scheduled it, and five runs — one
/// failed and retried — each a branch with its run message and answer. Its schedule is marked
/// <see cref="ChatSchedule.Demo"/>, so the dispatcher starts nothing for it. See
/// docs/35-scheduled-chats.md.
/// </summary>
public interface IScheduleDemo
{
    Task<ChatDetails> CreateAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>Whether a chat is the guide's demo nobody has changed or written in, which the guide removes.</summary>
    bool IsUntouched(ChatDetails chat);
}

public sealed class ScheduleDemo(
    IChatService chats,
    IChatScheduleStore store,
    IScheduleCalendar calendar,
    IScheduleDescriptions descriptions,
    IClock clock) : IScheduleDemo
{
    public const string Title = "Guide schedule demo";

    private const string RunTask = "Get today's euro exchange rate and post it with a link to the source.";

    private const string Request = "Every weekday at 9:00, get today's euro exchange rate and post it with a link to the source.";

    private const string Answer = "Scheduled: every weekday at 09:00 this chat forks a run from here, gets the rate and posts it. "
        + "A run that fails is retried once, 15 minutes later.\n\n"
        + "This chat was set up by the application guide to show scheduled chats. Nothing ran a model and no run will start; "
        + "it is removed when the tour ends unless you change its schedule or write in it.";

    private sealed record DemoRun(int Number, int DaysAgo, int Minute, int Attempt, ScheduleRunStatus Status, string Summary, string Reply);

    private static readonly DemoRun[] Runs =
    [
        new(1, 4, 0, 1, ScheduleRunStatus.Succeeded, "1 EUR = 1.0842 USD · ecb.europa.eu",
            "Today's rate: **1 EUR = 1.0842 USD** ([ECB reference rate](https://www.ecb.europa.eu)). The run succeeded."),
        new(2, 3, 0, 1, ScheduleRunStatus.Failed, "The source did not answer in time",
            "The rate source did not answer within the time limit, so there is nothing to post. The run failed; the schedule retries it."),
        new(3, 3, 15, 2, ScheduleRunStatus.Succeeded, "1 EUR = 1.0857 USD · ecb.europa.eu",
            "Today's rate: **1 EUR = 1.0857 USD** ([ECB reference rate](https://www.ecb.europa.eu)). The retry succeeded."),
        new(4, 2, 0, 1, ScheduleRunStatus.Succeeded, "1 EUR = 1.0861 USD · ecb.europa.eu",
            "Today's rate: **1 EUR = 1.0861 USD** ([ECB reference rate](https://www.ecb.europa.eu)). The run succeeded."),
        new(5, 1, 0, 1, ScheduleRunStatus.Succeeded, "1 EUR = 1.0839 USD · ecb.europa.eu",
            "Today's rate: **1 EUR = 1.0839 USD** ([ECB reference rate](https://www.ecb.europa.eu)). The run succeeded.")
    ];

    /// <summary>What <see cref="CreateAsync"/> writes; more means the person has written in it.</summary>
    private static readonly int InitialMessages = 2 + Runs.Length * 2;

    public async Task<ChatDetails> CreateAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var zone = calendar.LocalTimeZoneId;
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(calendar.ToLocal(now, zone));
        var settings = new ChatScheduleSettings(RunTask,
            new ScheduleRecurrence(ScheduleFrequency.Daily, today.AddDays(-7).ToString(ScheduleCalendar.DateFormat, CultureInfo.InvariantCulture),
                "09:00", Weekdays: [ScheduleWeekday.Monday, ScheduleWeekday.Tuesday, ScheduleWeekday.Wednesday,
                    ScheduleWeekday.Thursday, ScheduleWeekday.Friday]),
            zone, "The post names the rate and links its source", new ScheduleRetry(1, 15),
            new ScheduleBranchRetention(new ScheduleRetentionRule(ScheduleRetentionAction.Keep),
                new ScheduleRetentionRule(ScheduleRetentionAction.Keep), new ScheduleRetentionRule(ScheduleRetentionAction.Keep)));

        var chat = await chats.CreateAsync(projectId, new CreateChatRequest(Title), cancellationToken);
        var request = Guid.CreateVersion7();
        chat = await AppendAsync(chat, new AppendChatMessageRequest(request, null, "User", Request, chat.Revision), cancellationToken);
        var answer = Guid.CreateVersion7();
        chat = await AppendAsync(chat, new AppendChatMessageRequest(answer, request, "Assistant", Answer, chat.Revision), cancellationToken);

        var records = new List<ScheduleRunRecord>();
        foreach (var run in Runs)
        {
            var local = today.AddDays(-run.DaysAgo).ToDateTime(new TimeOnly(9, run.Minute));
            var started = new DateTimeOffset(local, (calendar.FindTimeZone(zone) ?? TimeZoneInfo.Local).GetUtcOffset(local));
            var branch = Guid.CreateVersion7();
            var record = new ScheduleRunRecord(Guid.CreateVersion7(), branch, run.Number, started.AddMinutes(-run.Minute), started,
                run.Attempt, run.Status, FinishedAt: started.AddMinutes(1), Summary: run.Summary);
            var message = $"Scheduled run #{run.Number} · {descriptions.Moment(local)} ({zone})"
                + (run.Attempt > 1 ? $" · attempt {run.Attempt} of 2" : string.Empty)
                + $"\n\nTask: {RunTask}\n\nSuccess criteria: {settings.SuccessCriteria}\n\n"
                + "Follow the chat-schedule-run skill: carry out the task, then report the outcome with app_schedule ReportRun.";
            chat = await AppendAsync(chat, new AppendChatMessageRequest(branch, answer, "User", message, chat.Revision,
                BranchId: branch, ParentBranchId: chat.Id, BranchTitle: descriptions.RunTitle(record, local, 1, run.Status)), cancellationToken);
            chat = await AppendAsync(chat, new AppendChatMessageRequest(Guid.CreateVersion7(), branch, "Assistant", run.Reply,
                chat.Revision, BranchId: branch), cancellationToken);
            records.Add(record);
        }

        var schedule = new ChatSchedule(settings, NextRunAt: calendar.NextAfter(settings.Recurrence, zone, now),
            Occurrences: Runs.Length, RunNumber: Runs.Length, Runs: records, Demo: true);
        _ = await store.UpdateAsync(projectId, chat.Id, _ => schedule, cancellationToken)
            ?? throw new InvalidOperationException("The schedule demo chat could not be written.");
        return await chats.GetAsync(projectId, chat.Id, cancellationToken)
            ?? throw new InvalidOperationException("The schedule demo chat could not be written.");
    }

    public bool IsUntouched(ChatDetails chat) =>
        chat is { Title: Title, Kind: ChatSchedule.Kind } && chat.Messages.Count <= InitialMessages
        && store.Parse(chat.KindState) is { Demo: true };

    private async Task<ChatDetails> AppendAsync(ChatDetails chat, AppendChatMessageRequest request, CancellationToken cancellationToken) =>
        await chats.AppendMessageAsync(chat.ProjectId, chat.Id, request, cancellationToken)
        ?? throw new InvalidOperationException("The schedule demo chat could not be written.");
}
