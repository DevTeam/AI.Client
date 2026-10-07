namespace AI.Infrastructure.Tests.Storage;

using AI.Contracts.Chats;
using AI.Contracts.Runs;
using AI.Contracts.Schedules;
using AI.Contracts.Settings;
using Shouldly;
using Xunit;

/// <summary>
/// Scheduled chats through the shipped graph: a conversation becomes scheduled with its history,
/// the dispatcher's pass forks it from its end at an occurrence, the run's report decides its
/// outcome, and the retention, retry and skip rules act on what the run left behind. The pass is
/// driven with explicit moments, so no test waits for a clock.
/// </summary>
public sealed partial class ChatExecutionTests
{
    [Fact]
    public async Task ScheduledRunForksFromTheEndReportsSuccessAndDeletesItsBranch()
    {
        await using var fixture = await ScheduledFixtureAsync();
        var view = await fixture.Schedules.SetAsync(fixture.ProjectId, fixture.ChatId,
            new SetChatScheduleRequest(Settings(retry: null)), CancellationToken.None);
        view.ShouldNotBeNull().ChatKind.ShouldBe(ChatSchedule.Kind);
        var due = view.Schedule.ShouldNotBeNull().NextRunAt.ShouldNotBeNull();
        (await fixture.Chats.ListAsync(fixture.ProjectId, CancellationToken.None)).Single().Kind.ShouldBe(ChatSchedule.Kind);
        var mainHead = await MainHeadAsync(fixture);

        await fixture.SchedulePass.ProcessAsync(fixture.ProjectId, fixture.ChatId, due, CancellationToken.None);
        var call = await fixture.NextCallAsync();
        call.Request.ContextMessages!.ShouldContain(message => message.Content.Contains("Check the nightly build"));
        call.Request.ContextMessages!.ShouldContain(message => message.Content.Contains("Scheduled run #1"));
        var run = (await ScheduleAsync(fixture)).ActiveRun.ShouldNotBeNull();
        run.Number.ShouldBe(1);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        var branch = chat!.Branches!.Single(item => item.Id == run.BranchId);
        branch.Title.ShouldStartWith("#1 · ");
        chat.Messages.Single(message => message.Id == run.BranchId).ParentId.ShouldBe(mainHead);

        await fixture.Schedules.ReportRunAsync(fixture.ProjectId, fixture.ChatId, run.BranchId!.Value, true, "Build is green",
            null, CancellationToken.None);
        call.Answer.SetResult("The build is green.");
        await fixture.WaitAsync(snapshot => snapshot.BranchId == run.BranchId && snapshot.Status == ChatRunStatus.Completed);

        await fixture.SchedulePass.ProcessAsync(fixture.ProjectId, fixture.ChatId, due.AddMinutes(1), CancellationToken.None);
        var finished = (await ScheduleAsync(fixture)).Runs!.Single();
        finished.Status.ShouldBe(ScheduleRunStatus.Succeeded);
        finished.Summary.ShouldBe("Build is green");
        finished.BranchDeleteAt.ShouldNotBeNull();

        await fixture.SchedulePass.ProcessAsync(fixture.ProjectId, fixture.ChatId, due.AddMinutes(2), CancellationToken.None);
        (await ScheduleAsync(fixture)).Runs!.Single().BranchDeleted.ShouldBeTrue();
        chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Branches!.ShouldNotContain(item => item.Id == run.BranchId);
        chat.Messages.ShouldContain(message => message.Content == "Check the nightly build");
    }

    [Fact]
    public async Task ScheduledRunThatEndsWithoutAReportFailsKeepsItsBranchAndRetries()
    {
        await using var fixture = await ScheduledFixtureAsync();
        var view = await fixture.Schedules.SetAsync(fixture.ProjectId, fixture.ChatId,
            new SetChatScheduleRequest(Settings(retry: new ScheduleRetry(1, 5))), CancellationToken.None);
        var due = view!.Schedule!.NextRunAt!.Value;

        await fixture.SchedulePass.ProcessAsync(fixture.ProjectId, fixture.ChatId, due, CancellationToken.None);
        var first = (await ScheduleAsync(fixture)).ActiveRun!;
        (await fixture.NextCallAsync()).Answer.SetResult("I looked at it.");
        await fixture.WaitAsync(snapshot => snapshot.BranchId == first.BranchId && snapshot.Status == ChatRunStatus.Completed);

        var finishedAt = due.AddMinutes(1);
        await fixture.SchedulePass.ProcessAsync(fixture.ProjectId, fixture.ChatId, finishedAt, CancellationToken.None);
        var schedule = await ScheduleAsync(fixture);
        var failed = schedule.Runs!.Single();
        failed.Status.ShouldBe(ScheduleRunStatus.Failed);
        failed.Summary!.ShouldContain("without reporting");
        failed.BranchDeleteAt.ShouldBeNull();
        schedule.RetryAt.ShouldBe(finishedAt.AddMinutes(5));
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Branches!.Single(item => item.Id == failed.BranchId).Title.ShouldEndWith("· failed");

        await fixture.SchedulePass.ProcessAsync(fixture.ProjectId, fixture.ChatId, schedule.RetryAt!.Value, CancellationToken.None);
        (await fixture.NextCallAsync()).Request.ContextMessages!.ShouldContain(message => message.Content.Contains("attempt 2 of 2"));
        var retry = (await ScheduleAsync(fixture)).ActiveRun!;
        retry.Attempt.ShouldBe(2);
        retry.ScheduledFor.ShouldBe(failed.ScheduledFor);
        chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Branches!.Single(item => item.Id == retry.BranchId).Title.ShouldContain("retry 1/1");
        // The retry forks from the end of the main branch, not from the failed run.
        chat.Messages.Single(message => message.Id == retry.BranchId).ParentId.ShouldBe(await MainHeadAsync(fixture));
    }

    [Fact]
    public async Task OccurrenceWhileARunGoesIsSkippedAndOneFoundLateIsMissed()
    {
        await using var fixture = await ScheduledFixtureAsync();
        var settings = Settings(retry: null) with
        {
            Recurrence = new ScheduleRecurrence(ScheduleFrequency.Hourly, Tomorrow(), "09:00")
        };
        var due = (await fixture.Schedules.SetAsync(fixture.ProjectId, fixture.ChatId, new SetChatScheduleRequest(settings),
            CancellationToken.None))!.Schedule!.NextRunAt!.Value;

        await fixture.SchedulePass.ProcessAsync(fixture.ProjectId, fixture.ChatId, due, CancellationToken.None);
        var call = await fixture.NextCallAsync();
        await fixture.SchedulePass.ProcessAsync(fixture.ProjectId, fixture.ChatId, due.AddHours(1), CancellationToken.None);
        var schedule = await ScheduleAsync(fixture);
        schedule.Runs!.ShouldContain(run => run.Status == ScheduleRunStatus.Skipped && run.ScheduledFor == due.AddHours(1));
        schedule.NextRunAt.ShouldBe(due.AddHours(2));

        call.Answer.SetResult("Done.");
        var active = schedule.ActiveRun!;
        await fixture.WaitAsync(snapshot => snapshot.BranchId == active.BranchId && snapshot.Status == ChatRunStatus.Completed);
        await fixture.SchedulePass.ProcessAsync(fixture.ProjectId, fixture.ChatId, due.AddHours(5), CancellationToken.None);
        schedule = await ScheduleAsync(fixture);
        schedule.Runs!.ShouldContain(run => run.Status == ScheduleRunStatus.Missed && run.ScheduledFor == due.AddHours(2));
        schedule.NextRunAt.ShouldBe(due.AddHours(6));
        schedule.ActiveRun.ShouldBeNull();
    }

    [Fact]
    public async Task PausingSkipsOccurrencesAndRemovingKeepsTheConversation()
    {
        await using var fixture = await ScheduledFixtureAsync();
        var view = await fixture.Schedules.SetAsync(fixture.ProjectId, fixture.ChatId,
            new SetChatScheduleRequest(Settings(retry: null)), CancellationToken.None);
        var due = view!.Schedule!.NextRunAt!.Value;

        var paused = await fixture.Schedules.PauseAsync(fixture.ProjectId, fixture.ChatId, true, view.Schedule.Revision, CancellationToken.None);
        paused!.Schedule!.Paused.ShouldBeTrue();
        await Should.ThrowAsync<AI.Application.Schedules.ScheduleConflictException>(() =>
            fixture.Schedules.PauseAsync(fixture.ProjectId, fixture.ChatId, false, view.Schedule.Revision, CancellationToken.None));
        await fixture.SchedulePass.ProcessAsync(fixture.ProjectId, fixture.ChatId, due, CancellationToken.None);
        (await ScheduleAsync(fixture)).Runs.ShouldBeEmpty();

        var removed = await fixture.Schedules.RemoveAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        removed!.ChatKind.ShouldBe("conversation");
        removed.Schedule.ShouldBeNull();
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Kind.ShouldBe("conversation");
        chat.Messages.ShouldContain(message => message.Content == "Check the nightly build");
    }

    [Fact]
    public async Task RunThatGoesIsWatchedAgainAFewSecondsAfterItWasLastLookedAt()
    {
        await using var fixture = await ScheduledFixtureAsync();
        var now = DateTimeOffset.UtcNow;
        var running = new ChatSchedule(Settings(retry: null), NextRunAt: now.AddHours(1), Runs:
            [new ScheduleRunRecord(Guid.NewGuid(), Guid.NewGuid(), 1, now, now, 1, ScheduleRunStatus.Running)]);

        // Never looked at: due at once. Looked at just now: due again shortly, not "a moment from now" forever.
        fixture.SchedulePass.DueAt(running, now, null).ShouldBe(now);
        var watched = fixture.SchedulePass.DueAt(running, now, now).ShouldNotBeNull();
        watched.ShouldBeGreaterThan(now);
        fixture.SchedulePass.DueAt(running, watched, now).ShouldBe(watched);
        fixture.SchedulePass.DueAt(running with { Runs = [] }, now, now).ShouldBe(now.AddHours(1));
        fixture.SchedulePass.DueAt(running with { Runs = [], Paused = true }, now, now).ShouldBeNull();
    }

    [Fact]
    public async Task ScheduleRejectsATimeThatHasPassedAndAKindThatCannotBeScheduled()
    {
        await using var fixture = await ScheduledFixtureAsync();
        var past = Settings(retry: null) with
        {
            Recurrence = new ScheduleRecurrence(ScheduleFrequency.Once, "2020-01-01", "09:00")
        };
        (await Should.ThrowAsync<ArgumentException>(() => fixture.Schedules.SetAsync(fixture.ProjectId, fixture.ChatId,
            new SetChatScheduleRequest(past), CancellationToken.None))).Message.ShouldContain("no occurrence after now");
        await Should.ThrowAsync<ArgumentException>(() => fixture.Schedules.SetAsync(fixture.ProjectId, fixture.ChatId,
            new SetChatScheduleRequest(Settings(retry: null) with { Task = " " }), CancellationToken.None));
        (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None))!.Kind.ShouldBe("conversation");

        var empty = await fixture.Chats.CreateAsync(fixture.ProjectId, new CreateChatRequest("Empty"), CancellationToken.None);
        await fixture.Schedules.SetAsync(fixture.ProjectId, empty.Id, new SetChatScheduleRequest(Settings(retry: null)), CancellationToken.None);
        // An empty chat gets its task as the first message: the instruction runs fork from.
        (await fixture.Chats.GetAsync(fixture.ProjectId, empty.Id, CancellationToken.None))!.Messages
            .ShouldHaveSingleItem().Content.ShouldBe("Check the nightly build and report failures.");
    }

    private static async Task<Fixture> ScheduledFixtureAsync()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.SetChatAutomationAsync(new ChatAutomationSettings(AutoTitle: false, SuggestReplies: false));
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Check the nightly build"));
        (await fixture.NextCallAsync()).Answer.SetResult("I will check it whenever this chat runs.");
        await fixture.WaitAsync(run => run.ChatId == fixture.ChatId && run.Status == ChatRunStatus.Completed);
        return fixture;
    }

    private static ChatScheduleSettings Settings(ScheduleRetry? retry) => new(
        "Check the nightly build and report failures.",
        new ScheduleRecurrence(ScheduleFrequency.Daily, Tomorrow(), "09:00"),
        "UTC",
        "The build log has no errors",
        retry,
        new ScheduleBranchRetention(new ScheduleRetentionRule(ScheduleRetentionAction.Delete),
            new ScheduleRetentionRule(ScheduleRetentionAction.Keep), new ScheduleRetentionRule(ScheduleRetentionAction.Keep)));

    private static string Tomorrow() => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)
        .ToString(ScheduleCalendar.DateFormat, System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<ChatSchedule> ScheduleAsync(Fixture fixture) =>
        (await fixture.Schedules.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None))!.Schedule!;

    private static async Task<Guid> MainHeadAsync(Fixture fixture) =>
        (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None))!
        .Branches!.Single(branch => branch.Id == fixture.ChatId).HeadMessageId!.Value;
}
