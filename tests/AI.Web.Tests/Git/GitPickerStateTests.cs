namespace AI.Web.Tests.Git;

using AI.Contracts.Git;
using AI.Web.Git;
using Moq;
using Shouldly;
using Xunit;

public sealed class GitPickerStateTests
{
    [Fact]
    public async Task MultipleSelectionShouldSurviveFilteringAndPaginationInClickOrder()
    {
        var api = new Mock<IGitApi>();
        api.Setup(item => item.CommitsAsync("repo", null, 0, CancellationToken.None))
            .ReturnsAsync(new GitListing("repo", [new("one", "First"), new("two", "Second")], true));
        api.Setup(item => item.CommitsAsync("repo", null, 2, CancellationToken.None))
            .ReturnsAsync(new GitListing("repo", [new("three", "Third")], false));
        var picker = new GitPickerState(api.Object);
        await picker.OpenAsync("repo", "commit", true, null, [], CancellationToken.None);
        picker.Toggle("two");
        picker.Filter = "First";
        picker.Items.ShouldHaveSingleItem().Value.ShouldBe("one");
        picker.Toggle("one");
        await picker.LoadMoreAsync(CancellationToken.None);
        picker.Toggle("three");
        picker.Values.ShouldBe(["two", "one", "three"]);
        picker.HasMore.ShouldBeFalse();
        picker.Toggle("one");
        picker.Values.ShouldBe(["two", "three"]);
    }

    [Fact]
    public async Task SingleSelectionShouldReplaceThePreviousBranchAndKeepCaseDistinct()
    {
        var api = new Mock<IGitApi>();
        api.Setup(item => item.BranchesAsync("repo", CancellationToken.None))
            .ReturnsAsync(new GitListing("repo", [new("refs/heads/Fix", "Fix"), new("refs/heads/fix", "fix")], false));
        var picker = new GitPickerState(api.Object);
        await picker.OpenAsync("repo", "branch", false, null, [], CancellationToken.None);
        picker.Toggle("refs/heads/Fix");
        picker.Toggle("refs/heads/fix");
        picker.Values.ShouldBe(["refs/heads/fix"]);
        picker.Toggle("not-listed");
        picker.Values.ShouldBe(["refs/heads/fix"]);
    }

    [Fact]
    public async Task ChangingTheHistoryBranchShouldReloadWhileKeepingChosenCommits()
    {
        var api = new Mock<IGitApi>();
        api.Setup(item => item.CommitsAsync("repo", null, 0, CancellationToken.None))
            .ReturnsAsync(new GitListing("repo", [new("one", "First")], false));
        api.Setup(item => item.CommitsAsync("repo", "refs/heads/topic", 0, CancellationToken.None))
            .ReturnsAsync(new GitListing("repo", [new("two", "Second")], false));
        var picker = new GitPickerState(api.Object);
        await picker.OpenAsync("repo", "commit", true, null, [], CancellationToken.None);
        picker.Toggle("one");
        await picker.ChangeRevisionAsync("refs/heads/topic", CancellationToken.None);
        picker.Items.ShouldHaveSingleItem().Value.ShouldBe("two");
        picker.Toggle("two");
        picker.Values.ShouldBe(["one", "two"]);
    }

    [Fact]
    public async Task FailedPageShouldLeaveTheSelectionAndBeRetryableAtTheSameOffset()
    {
        var api = new Mock<IGitApi>();
        api.SetupSequence(item => item.CommitsAsync("repo", null, 0, CancellationToken.None))
            .ThrowsAsync(new HttpRequestException("Offline"))
            .ReturnsAsync(new GitListing("repo", [new("one", "First")], false));
        var picker = new GitPickerState(api.Object);
        await picker.OpenAsync("repo", "commit", true, null, ["chosen"], CancellationToken.None);
        picker.ErrorMessage.ShouldBe("Offline");
        picker.IsLoading.ShouldBeFalse();
        await picker.LoadMoreAsync(CancellationToken.None);
        picker.ErrorMessage.ShouldBeNull();
        picker.Items.ShouldHaveSingleItem();
        picker.Values.ShouldBe(["chosen"]);
    }

    [Fact]
    public async Task ChoicesShouldKeepTheirLabelsAfterTheListIsReloadedAndBeClearable()
    {
        var api = new Mock<IGitApi>();
        api.Setup(item => item.CommitsAsync("repo", null, 0, CancellationToken.None))
            .ReturnsAsync(new GitListing("repo", [new("one", "abc1234 First")], false));
        api.Setup(item => item.CommitsAsync("repo", "topic", 0, CancellationToken.None))
            .ReturnsAsync(new GitListing("repo", [new("two", "def5678 Second")], false));
        var picker = new GitPickerState(api.Object);
        await picker.OpenAsync("repo", "commit", true, null, ["earlier"], CancellationToken.None);
        picker.Toggle("one");
        await picker.ChangeRevisionAsync("topic", CancellationToken.None);
        picker.LabelOf("one").ShouldBe("abc1234 First");
        picker.LabelOf("earlier").ShouldBe("earlier");
        picker.Clear();
        picker.Values.ShouldBeEmpty();
    }
}
