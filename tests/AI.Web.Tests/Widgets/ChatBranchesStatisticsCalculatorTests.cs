namespace AI.Web.Tests.Widgets;

using AI.Contracts.Chats;
using AI.Web.Widgets;
using Shouldly;
using Xunit;

public class ChatBranchesStatisticsCalculatorTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private readonly ChatBranchesStatisticsCalculator _calculator = new();

    [Fact]
    public void ShouldReturnEmptyWhenNoBranchesAreStored()
    {
        var stats = _calculator.Calculate(branches: null, messages: [], selectedBranchId: null);
        stats.ShouldBe(ChatBranchesStatistics.Empty);
        stats.HasBranches.ShouldBeFalse();
    }

    [Fact]
    public void ShouldReturnEmptyWhenBranchesListIsEmpty()
    {
        var stats = _calculator.Calculate(branches: [], messages: [], selectedBranchId: null);
        stats.ShouldBe(ChatBranchesStatistics.Empty);
    }

    [Fact]
    public void ShouldListOneRowPerStoredBranchWithStoredOrder()
    {
        var root = Branch(title: "Root");
        var alt = Branch(title: "Alternative take", parent: root.Id);
        var alt2 = Branch(title: "Second alternative", parent: root.Id);

        var stats = _calculator.Calculate([root, alt, alt2], messages: [], selectedBranchId: null);

        stats.HasBranches.ShouldBeTrue();
        stats.Branches.Count.ShouldBe(3);
        stats.Branches.Select(item => item.Title).ShouldBe(["Root", "Alternative take", "Second alternative"]);
    }

    [Fact]
    public void ShouldCountDescendantsByWalkingParentChain()
    {
        // Tree:
        //   root message A
        //     ├ B (User, root of branch X)
        //     │  └ C (Assistant, head of X)
        //     └ D (User, root of branch Y)
        //        └ E (Assistant, head of Y)
        var a = Message(parent: null, at: T0);
        var b = Message(parent: a.Id, at: T0.AddMinutes(1));
        var c = Message(parent: b.Id, at: T0.AddMinutes(2));
        var d = Message(parent: a.Id, at: T0.AddMinutes(3));
        var e = Message(parent: d.Id, at: T0.AddMinutes(4));
        var x = Branch(title: "X", rootId: b.Id, headId: c.Id);
        var y = Branch(title: "Y", rootId: d.Id, headId: e.Id);

        var stats = _calculator.Calculate([x, y], [a, b, c, d, e], selectedBranchId: null);

        var xRow = stats.Branches.Single(item => item.Title == "X");
        var yRow = stats.Branches.Single(item => item.Title == "Y");
        // X owns B and C (its root + its descendant); Y owns D and E.
        xRow.MessageCount.ShouldBe(2);
        yRow.MessageCount.ShouldBe(2);
    }

    [Fact]
    public void ShouldReportZeroMessagesWhenHeadIsMissing()
    {
        var branch = Branch(title: "Orphan", rootId: Guid.NewGuid(), headId: Guid.NewGuid());

        var stats = _calculator.Calculate([branch], messages: [], selectedBranchId: null);

        stats.Branches.ShouldHaveSingleItem().MessageCount.ShouldBe(0);
    }

    [Fact]
    public void ShouldUseRootMessageIdWhenAvailable()
    {
        var a = Message(parent: null, at: T0);
        var b = Message(parent: a.Id, at: T0.AddMinutes(1));
        var c = Message(parent: b.Id, at: T0.AddMinutes(2));
        // RootMessageId is set; the walk from head b→c→a would also find a, but rootMessageId
        // should be the source of truth.
        var branch = Branch(title: "Tracked", rootId: b.Id, headId: c.Id);

        var stats = _calculator.Calculate([branch], [a, b, c], selectedBranchId: null);

        var row = stats.Branches.ShouldHaveSingleItem();
        row.MessageCount.ShouldBe(2); // b + c, starting from b
    }

    [Fact]
    public void ShouldWalkBackFromHeadWhenRootMessageIdIsUnknown()
    {
        var a = Message(parent: null, at: T0);
        var b = Message(parent: a.Id, at: T0.AddMinutes(1));
        var c = Message(parent: b.Id, at: T0.AddMinutes(2));
        // RootMessageId is null → walk from head c back through b to a, which is the root.
        var branch = Branch(title: "Walked", headId: c.Id);

        var stats = _calculator.Calculate([branch], [a, b, c], selectedBranchId: null);

        stats.Branches.ShouldHaveSingleItem().MessageCount.ShouldBe(3);
    }

    [Fact]
    public void ShouldReportDepthFromParentChain()
    {
        var root = Branch(title: "Root");
        var child = Branch(title: "Child", parent: root.Id);
        var grandchild = Branch(title: "Grand", parent: child.Id);
        var sibling = Branch(title: "Sibling", parent: root.Id);

        var stats = _calculator.Calculate([root, child, grandchild, sibling], messages: [], selectedBranchId: null);

        stats.Branches.Single(item => item.Title == "Root").Depth.ShouldBe(0);
        stats.Branches.Single(item => item.Title == "Child").Depth.ShouldBe(1);
        stats.Branches.Single(item => item.Title == "Grand").Depth.ShouldBe(2);
        stats.Branches.Single(item => item.Title == "Sibling").Depth.ShouldBe(1);
    }

    [Fact]
    public void ShouldTolerateCyclesInTheParentChain()
    {
        // A cycle can only happen when two branches point at each other as parent. The visited
        // set guards the depth walk, and the depth should still be finite.
        var a = Branch(title: "A");
        var b = Branch(title: "B", parent: a.Id);
        a = a with { ParentBranchId = b.Id };

        var stats = _calculator.Calculate([a, b], messages: [], selectedBranchId: null);

        stats.Branches.Select(item => item.Title).ShouldBe(["A", "B"]);
        stats.Branches.Single(item => item.Title == "A").Depth.ShouldBeGreaterThanOrEqualTo(0);
        stats.Branches.Single(item => item.Title == "B").Depth.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void ShouldCountChildBranches()
    {
        var root = Branch(title: "Root");
        var firstChild = Branch(title: "First", parent: root.Id);
        var secondChild = Branch(title: "Second", parent: root.Id);
        var grandchild = Branch(title: "Grand", parent: firstChild.Id);

        var stats = _calculator.Calculate([root, firstChild, secondChild, grandchild], messages: [], selectedBranchId: null);

        stats.Branches.Single(item => item.Title == "Root").ChildCount.ShouldBe(2);
        stats.Branches.Single(item => item.Title == "First").ChildCount.ShouldBe(1);
        stats.Branches.Single(item => item.Title == "Second").ChildCount.ShouldBe(0);
        stats.Branches.Single(item => item.Title == "Grand").ChildCount.ShouldBe(0);
    }

    [Fact]
    public void ShouldReadHeadTimestampFromMessage()
    {
        var a = Message(parent: null, at: T0);
        var b = Message(parent: a.Id, at: T0.AddMinutes(2));
        var c = Message(parent: b.Id, at: T0.AddMinutes(5));
        var branch = Branch(title: "T", rootId: a.Id, headId: c.Id);

        var stats = _calculator.Calculate([branch], [a, b, c], selectedBranchId: null);

        stats.Branches.ShouldHaveSingleItem().HeadAt.ShouldBe(T0.AddMinutes(5));
    }

    [Fact]
    public void ShouldLeaveHeadAtNullWhenHeadMessageIsUnknown()
    {
        var branch = Branch(title: "NoHead", rootId: Guid.NewGuid(), headId: Guid.NewGuid());

        var stats = _calculator.Calculate([branch], messages: [], selectedBranchId: null);

        stats.Branches.ShouldHaveSingleItem().HeadAt.ShouldBeNull();
    }

    [Fact]
    public void ShouldMarkTheActiveBranchBySelectedBranchId()
    {
        var a = Branch(title: "A");
        var b = Branch(title: "B");

        var stats = _calculator.Calculate([a, b], messages: [], selectedBranchId: b.Id);

        stats.Branches.Single(item => item.Title == "A").IsActive.ShouldBeFalse();
        stats.Branches.Single(item => item.Title == "B").IsActive.ShouldBeTrue();
    }

    [Fact]
    public void ShouldMarkEveryRowInactiveWhenNoBranchIsSelected()
    {
        var a = Branch(title: "A");
        var b = Branch(title: "B");

        var stats = _calculator.Calculate([a, b], messages: [], selectedBranchId: null);

        foreach (var item in stats.Branches)
            item.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void ShouldFallBackToGenericTitleWhenStoredTitleIsBlank()
    {
        var branch = Branch(title: "   ");

        var stats = _calculator.Calculate([branch], messages: [], selectedBranchId: null);

        stats.Branches.ShouldHaveSingleItem().Title.ShouldBe("Branch");
    }

    private static ChatBranchView Branch(string title, Guid? parent = null, Guid? rootId = null, Guid? headId = null) =>
        new(
            Id: Guid.NewGuid(),
            HeadMessageId: headId,
            Title: title,
            ParentBranchId: parent,
            RootMessageId: rootId);

    private static ChatMessageView Message(Guid? parent, DateTimeOffset at) =>
        new(Guid.NewGuid(), parent, parent is null ? "Assistant" : "User", "", at);
}
