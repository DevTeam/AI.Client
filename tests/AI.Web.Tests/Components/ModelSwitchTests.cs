using AI.Contracts.Chats;
using AI.Contracts.Usage;
using AI.Web.Components;
using Shouldly;
using Xunit;

namespace AI.Web.Tests.Components;

public sealed class ModelSwitchTests
{
    private readonly ChatFeed _feed = new();
    private readonly DateTimeOffset _at = new(2026, 10, 3, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShouldShowOnlyChangesBetweenModelsActuallyUsedOnTheVisibleBranch()
    {
        var first = User();
        var unused = User();
        var same = User();
        var changed = User();
        var otherBranch = User();
        var usage = new[] { Usage(first, "a"), Usage(same, "a"), Usage(changed, "b"), Usage(otherBranch, "c") };

        var switches = _feed.BuildModelSwitches([first, unused, same, changed], usage, null);

        switches.Count.ShouldBe(1);
        switches[changed.Id].ShouldBe([new ModelSwitch("a", "b")]);
    }

    [Fact]
    public void ShouldMergeStoredAndLiveRequestsWithoutDuplicatingChangesOnRetry()
    {
        var first = User();
        var retry = User();
        var stored = Usage(retry, "b");
        var live = stored with
        {
            AnswerModels = [stored.AnswerModels![0], new AnswerModelUsage(Guid.NewGuid(), _at.AddSeconds(1), "c")]
        };

        var switches = _feed.BuildModelSwitches([first, retry], [Usage(first, "a"), stored], live);

        switches[retry.Id].ShouldBe([new ModelSwitch("a", "b"), new ModelSwitch("b", "c")]);
    }

    [Fact]
    public void ShouldHandleLegacyUsageAndFirstUseWithoutInventingASwitch()
    {
        var legacy = User();
        var first = User();
        var oldUsage = Usage(legacy, "old") with { AnswerModels = null };

        _feed.BuildModelSwitches([legacy, first], [oldUsage], Usage(first, "a")).ShouldBeEmpty();
    }

    private ChatMessageView User() => new(Guid.NewGuid(), null, "User", "question", _at);

    private TurnTokenUsage Usage(ChatMessageView user, string model) => new(user.Id, Guid.NewGuid(), _at,
        new TokenUsageTotals(new TokenCounts(10, 1), 1, 0, null, 0, 10), [],
        [new AnswerModelUsage(Guid.NewGuid(), _at, model)]);
}
