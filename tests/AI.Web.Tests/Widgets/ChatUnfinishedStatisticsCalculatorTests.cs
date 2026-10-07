namespace AI.Web.Tests.Widgets;

using AI.Contracts.Chats;
using AI.Contracts.Runs;
using AI.Web.Widgets;
using Shouldly;
using Xunit;

public class ChatUnfinishedStatisticsCalculatorTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private static readonly Guid ProjectId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ChatA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ChatB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BranchX = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly ChatUnfinishedStatisticsCalculator _calculator = new();

    [Fact]
    public void ShouldListNothingForFinishedRuns()
    {
        var stats = _calculator.Calculate([Chat(ChatA, "Alpha")], [Run(ChatA, ChatA, ChatRunStatus.Completed)]);

        stats.Tasks.ShouldBeEmpty();
        stats.HasTasks.ShouldBeFalse();
        stats.ChatsWithUnfinishedWork.ShouldBe(0);
        stats.Resumable.ShouldBe(0);
    }

    [Fact]
    public void ShouldListAnInterruptedRun()
    {
        var stats = _calculator.Calculate([Chat(ChatA, "Alpha")], [Run(ChatA, ChatA, ChatRunStatus.Interrupted)]);

        var task = stats.Tasks.ShouldHaveSingleItem();
        task.Reason.ShouldBe(ChatUnfinishedReason.Interrupted);
        task.ChatTitle.ShouldBe("Alpha");
        task.IsMainBranch.ShouldBeTrue();
        task.NeedsAttention.ShouldBeTrue();
        task.CanResume.ShouldBeTrue();
    }

    [Fact]
    public void ShouldRankWhatBlocksThePersonFirst()
    {
        var runs = new List<ChatRunSnapshot>
        {
            Run(ChatB, ChatB, ChatRunStatus.Completed, queue: [Queued()]),
            Run(ChatA, ChatA, ChatRunStatus.Generating),
            Run(ChatB, ChatB, ChatRunStatus.Interrupted),
            Run(ChatA, ChatA, ChatRunStatus.Completed, approval: Approval())
        };

        var tasks = _calculator.Calculate([Chat(ChatA, "Alpha"), Chat(ChatB, "Beta")], runs).Tasks;

        tasks.Select(task => task.Reason).ShouldBe(
        [
            ChatUnfinishedReason.Approval,
            ChatUnfinishedReason.Interrupted,
            ChatUnfinishedReason.Running,
            ChatUnfinishedReason.Queue
        ]);
    }

    [Fact]
    public void ShouldCountDistinctChatsAndResumableTasks()
    {
        var runs = new List<ChatRunSnapshot>
        {
            Run(ChatA, ChatA, ChatRunStatus.Interrupted),
            Run(ChatA, BranchX, ChatRunStatus.Interrupted),
            Run(ChatB, ChatB, ChatRunStatus.Generating)
        };

        var stats = _calculator.Calculate([Chat(ChatA, "Alpha"), Chat(ChatB, "Beta")], runs);

        stats.Tasks.Count.ShouldBe(3);
        stats.ChatsWithUnfinishedWork.ShouldBe(2);
        stats.Resumable.ShouldBe(2);
    }

    [Fact]
    public void ShouldNameTheBranchWhenTheRunIsNotOnTheChatItself()
    {
        var stats = _calculator.Calculate([Chat(ChatA, "Alpha")], [Run(ChatA, BranchX, ChatRunStatus.Paused)]);

        var task = stats.Tasks.ShouldHaveSingleItem();
        task.BranchId.ShouldBe(BranchX);
        task.IsMainBranch.ShouldBeFalse();
    }

    [Fact]
    public void ShouldLeaveOutChatsTheSidebarDoesNotShow()
    {
        var runs = new List<ChatRunSnapshot>
        {
            Run(ChatA, ChatA, ChatRunStatus.Interrupted),
            Run(ChatB, ChatB, ChatRunStatus.Interrupted)
        };

        var stats = _calculator.Calculate([Chat(ChatA, "Alpha"), Chat(ChatB, "Beta", archived: true)], runs);

        stats.Tasks.ShouldHaveSingleItem().ChatId.ShouldBe(ChatA);
    }

    [Fact]
    public void ShouldLeaveOutAnEmptyChatEvenWhenARunSnapshotNamesIt()
    {
        var stats = _calculator.Calculate([Chat(ChatA, "Alpha", empty: true)],
            [Run(ChatA, ChatA, ChatRunStatus.Interrupted)]);

        stats.Tasks.ShouldBeEmpty();
    }

    [Fact]
    public void ShouldNotOfferResumeForAFailureWithoutARecoveryAction()
    {
        var run = Run(ChatA, ChatA, ChatRunStatus.Failed);

        var stats = _calculator.Calculate([Chat(ChatA, "Alpha")], [run]);

        stats.Tasks.ShouldBeEmpty();
    }

    [Fact]
    public void ShouldOfferResumeForAFailureThatStillListsOne()
    {
        var run = Run(ChatA, ChatA, ChatRunStatus.Failed, recovery: [RunRecoveryAction.Resume]);

        var task = _calculator.Calculate([Chat(ChatA, "Alpha")], [run]).Tasks.ShouldHaveSingleItem();

        task.Reason.ShouldBe(ChatUnfinishedReason.Failed);
        task.NeedsAttention.ShouldBeTrue();
        task.CanResume.ShouldBeTrue();
    }

    [Fact]
    public void ShouldReportAPendingQuestionAsNeedingThePerson()
    {
        var run = Run(ChatA, ChatA, ChatRunStatus.Generating, prompt: Prompt());

        var task = _calculator.Calculate([Chat(ChatA, "Alpha")], [run]).Tasks.ShouldHaveSingleItem();

        task.Reason.ShouldBe(ChatUnfinishedReason.Prompt);
        task.NeedsAttention.ShouldBeTrue();
    }

    [Fact]
    public void ShouldReportAProviderWait()
    {
        var run = Run(ChatA, ChatA, ChatRunStatus.Generating,
            wait: new ChatRunWait(ChatRunWaitKind.RateLimit, T0.AddMinutes(1), 2));

        var task = _calculator.Calculate([Chat(ChatA, "Alpha")], [run]).Tasks.ShouldHaveSingleItem();

        task.Reason.ShouldBe(ChatUnfinishedReason.Wait);
    }

    [Fact]
    public void ShouldCarryTheChatsLastActivityTime()
    {
        var chat = Chat(ChatA, "Alpha") with { LastActivityAt = T0.AddHours(5) };

        var task = _calculator.Calculate([chat], [Run(ChatA, ChatA, ChatRunStatus.Interrupted)])
            .Tasks.ShouldHaveSingleItem();

        task.LastActivityAt.ShouldBe(T0.AddHours(5));
    }

    [Fact]
    public void ShouldNotOfferResumeForARunThatIsStillGenerating()
    {
        var run = Run(ChatA, ChatA, ChatRunStatus.Generating, queue: [Queued()]);

        var task = _calculator.Calculate([Chat(ChatA, "Alpha")], [run]).Tasks.ShouldHaveSingleItem();

        task.Reason.ShouldBe(ChatUnfinishedReason.Running);
        task.CanResume.ShouldBeFalse();
    }

    [Fact]
    public void ShouldNotOfferResumeForWhatWaitsOnAnAnswerOrALimit()
    {
        var runs = new List<ChatRunSnapshot>
        {
            Run(ChatA, ChatA, ChatRunStatus.Paused, approval: Approval()),
            Run(ChatA, BranchX, ChatRunStatus.Generating, prompt: Prompt()),
            Run(ChatB, ChatB, ChatRunStatus.Generating, wait: new ChatRunWait(ChatRunWaitKind.RateLimit, T0, 1))
        };

        var stats = _calculator.Calculate([Chat(ChatA, "Alpha"), Chat(ChatB, "Beta")], runs);

        stats.Tasks.ShouldAllBe(task => !task.CanResume);
        stats.Resumable.ShouldBe(0);
    }

    [Fact]
    public void ShouldNotOfferResumeForAFailureWithoutResumeEvenWithMessagesQueued()
    {
        var run = Run(ChatA, ChatA, ChatRunStatus.Failed, queue: [Queued()], recovery: [RunRecoveryAction.Retry]);

        var task = _calculator.Calculate([Chat(ChatA, "Alpha")], [run]).Tasks.ShouldHaveSingleItem();

        task.Reason.ShouldBe(ChatUnfinishedReason.Failed);
        task.CanResume.ShouldBeFalse();
    }

    [Fact]
    public void ShouldOfferResumeForMessagesQueuedBehindAnIdleChat()
    {
        var run = Run(ChatA, ChatA, ChatRunStatus.Completed, queue: [Queued()]);

        _calculator.Calculate([Chat(ChatA, "Alpha")], [run]).Tasks.ShouldHaveSingleItem().CanResume.ShouldBeTrue();
    }

    [Fact]
    public void ShouldNameTheToolWaitingForApproval()
    {
        var approval = Approval() with { CallIndex = 2, BatchSize = 3 };
        var run = Run(ChatA, ChatA, ChatRunStatus.Generating, approval: approval);

        _calculator.Calculate([Chat(ChatA, "Alpha")], [run]).Tasks.ShouldHaveSingleItem()
            .Detail.ShouldBe("read_file (2 of 3)");
    }

    [Fact]
    public void ShouldQuoteTheFirstQuestionOnOneLine()
    {
        var prompt = new UserPrompt(Guid.NewGuid(), [Question("Which\n  folder?"), Question("Why?")], 0);
        var run = Run(ChatA, ChatA, ChatRunStatus.Generating, prompt: prompt);

        _calculator.Calculate([Chat(ChatA, "Alpha")], [run]).Tasks.ShouldHaveSingleItem()
            .Detail.ShouldBe("Which folder? (+1 more)");
    }

    [Fact]
    public void ShouldShortenALongFailureMessage()
    {
        var error = string.Join(' ', Enumerable.Repeat("word", 60));
        var run = Run(ChatA, ChatA, ChatRunStatus.Failed, recovery: [RunRecoveryAction.Retry]) with { Error = error };

        var detail = _calculator.Calculate([Chat(ChatA, "Alpha")], [run]).Tasks.ShouldHaveSingleItem().Detail;

        detail.ShouldNotBeNull();
        detail.Length.ShouldBeLessThanOrEqualTo(121);
        detail.ShouldEndWith("word…");
    }

    [Fact]
    public void ShouldPreviewTheNextQueuedMessage()
    {
        var run = Run(ChatA, ChatA, ChatRunStatus.Interrupted, queue: [Queued()]);

        _calculator.Calculate([Chat(ChatA, "Alpha")], [run]).Tasks.ShouldHaveSingleItem().Detail.ShouldBe("later");
    }

    [Fact]
    public void ShouldCarryWhenAWaitingRunTriesAgain()
    {
        var run = Run(ChatA, ChatA, ChatRunStatus.Generating,
            wait: new ChatRunWait(ChatRunWaitKind.RateLimit, T0.AddMinutes(3), 1));

        _calculator.Calculate([Chat(ChatA, "Alpha")], [run]).Tasks.ShouldHaveSingleItem()
            .RetryAt.ShouldBe(T0.AddMinutes(3));
    }

    [Fact]
    public void ShouldCountTheTasksThatNeedThePerson()
    {
        var runs = new List<ChatRunSnapshot>
        {
            Run(ChatA, ChatA, ChatRunStatus.Interrupted),
            Run(ChatA, BranchX, ChatRunStatus.Generating),
            Run(ChatB, ChatB, ChatRunStatus.Completed, approval: Approval())
        };

        _calculator.Calculate([Chat(ChatA, "Alpha"), Chat(ChatB, "Beta")], runs).NeedingAttention.ShouldBe(2);
    }

    [Fact]
    public void ShouldReportAnEmptyProjectAsAnEmptyList()
    {
        var stats = _calculator.Calculate([], []);

        stats.Tasks.ShouldBeEmpty();
        stats.ShouldBe(ChatUnfinishedStatistics.Empty);
    }

    private static ChatSummary Chat(Guid id, string title, bool empty = false, bool archived = false) =>
        new(id, ProjectId, title, T0, 1, T0, IsEmpty: empty, ArchivedAt: archived ? T0 : null);

    private static ChatRunSnapshot Run(Guid chatId, Guid branchId, ChatRunStatus status,
        IReadOnlyList<QueuedChatMessage>? queue = null, ToolApproval? approval = null,
        UserPrompt? prompt = null, ChatRunWait? wait = null,
        IReadOnlyList<RunRecoveryAction>? recovery = null) =>
        new(ProjectId, chatId, branchId, status, string.Empty, queue ?? [], false, null, 1,
            PendingApproval: approval, PendingPrompt: prompt, Wait: wait, RecoveryActions: recovery);

    private static QueuedChatMessage Queued() =>
        new(Guid.NewGuid(), "later", T0);

    private static ToolApproval Approval() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "read_file", "hash", "{}", 30);

    private static UserPrompt Prompt() =>
        new(Guid.NewGuid(), [], 0);

    private static UserPromptQuestion Question(string text) =>
        new(Guid.NewGuid().ToString(), text, null, [], false, false);
}
