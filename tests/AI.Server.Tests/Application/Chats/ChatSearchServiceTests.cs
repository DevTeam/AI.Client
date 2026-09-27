namespace AI.Application.Tests.Chats;

using AI.Application.Chats;
using AI.Application.Projects;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using Moq;
using Shouldly;
using Xunit;

public sealed class ChatSearchServiceTests
{
    private static readonly Guid ProjectA = Guid.Parse("019f0000-0000-7000-8000-0000000000a1");
    private static readonly Guid ProjectB = Guid.Parse("019f0000-0000-7000-8000-0000000000b1");
    private static readonly Guid ChatA = Guid.Parse("019f0000-0000-7000-8000-0000000000a2");
    private static readonly Guid ChatB = Guid.Parse("019f0000-0000-7000-8000-0000000000b2");

    [Fact]
    public async Task ShouldFindMessagesAcrossEveryProject()
    {
        var service = Build(
            (ProjectA, "Alpha", ChatA, "First chat", ["please deploy the service", "unrelated"]),
            (ProjectB, "Beta", ChatB, "Second chat", ["deploy again"]));

        var result = await service.SearchAsync(new ChatSearchRequest("deploy"), TestContext.Current.CancellationToken);

        result.Matches.Count.ShouldBe(2);
        result.Matches.Select(match => match.ProjectName).ShouldBe(["Alpha", "Beta"], ignoreOrder: true);
        result.ChatsSearched.ShouldBe(2);
        result.Truncated.ShouldBeFalse();
        result.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task ShouldStayInsideTheProjectItWasGiven()
    {
        var service = Build(
            (ProjectA, "Alpha", ChatA, "First chat", ["deploy here"]),
            (ProjectB, "Beta", ChatB, "Second chat", ["deploy there"]));

        var result = await service.SearchAsync(
            new ChatSearchRequest("deploy", ProjectA), TestContext.Current.CancellationToken);

        result.Matches.ShouldHaveSingleItem().ProjectName.ShouldBe("Alpha");
    }

    [Fact]
    public async Task ShouldIgnoreCaseByDefaultAndRespectItWhenAsked()
    {
        var service = Build((ProjectA, "Alpha", ChatA, "Chat", ["Deploy the service"]));

        (await service.SearchAsync(new ChatSearchRequest("deploy"), TestContext.Current.CancellationToken))
            .Matches.Count.ShouldBe(1);
        (await service.SearchAsync(new ChatSearchRequest("deploy", IgnoreCase: false), TestContext.Current.CancellationToken))
            .Matches.ShouldBeEmpty();
    }

    [Fact]
    public async Task ShouldLeaveToolResultsOutUnlessTheyAreAskedFor()
    {
        var service = Build([(ProjectA, "Alpha", ChatA, "Chat", [])],
            [new ChatMessageView(Guid.NewGuid(), null, "Tool", "deploy output", DateTimeOffset.UnixEpoch)]);

        // Tool messages are serialized call results, often huge; including them by default would
        // bury the conversation the person is actually looking for.
        (await service.SearchAsync(new ChatSearchRequest("deploy"), TestContext.Current.CancellationToken))
            .Matches.ShouldBeEmpty();
        (await service.SearchAsync(new ChatSearchRequest("deploy", Roles: ["Tool"]), TestContext.Current.CancellationToken))
            .Matches.ShouldHaveSingleItem().Role.ShouldBe("Tool");
    }

    [Fact]
    public async Task ShouldCountEveryOccurrenceAndQuoteOnlyTheFirst()
    {
        var service = Build((ProjectA, "Alpha", ChatA, "Chat",
            [new string('x', 400) + " deploy " + new string('y', 400) + " deploy"]));

        var match = (await service.SearchAsync(new ChatSearchRequest("deploy"), TestContext.Current.CancellationToken))
            .Matches.ShouldHaveSingleItem();

        match.MatchCount.ShouldBe(2);
        match.Snippet.Length.ShouldBeLessThan(ChatSearchLimits.SnippetLength + 4);
        match.Snippet.ShouldContain("deploy");
        match.Snippet.ShouldStartWith("…");
        match.Snippet.ShouldEndWith("…");
    }

    [Fact]
    public async Task ShouldResumeFromItsOwnCursorWithoutRepeatingMatches()
    {
        var service = Build((ProjectA, "Alpha", ChatA, "Chat", ["deploy one", "deploy two", "deploy three"]));

        var first = await service.SearchAsync(new ChatSearchRequest("deploy", Limit: 2), TestContext.Current.CancellationToken);
        first.Matches.Count.ShouldBe(2);
        first.Truncated.ShouldBeTrue();
        first.NextCursor.ShouldNotBeNull();

        var second = await service.SearchAsync(
            new ChatSearchRequest("deploy", Limit: 2, Cursor: first.NextCursor), TestContext.Current.CancellationToken);

        second.Matches.ShouldHaveSingleItem().Snippet.ShouldContain("three");
        second.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task ShouldReportAnUnusablePatternInsteadOfThrowing()
    {
        var service = Build((ProjectA, "Alpha", ChatA, "Chat", ["anything"]));

        var result = await service.SearchAsync(
            new ChatSearchRequest("(unclosed", IsRegex: true), TestContext.Current.CancellationToken);

        result.Error.ShouldNotBeNull();
        result.Matches.ShouldBeEmpty();
    }

    [Fact]
    public async Task ShouldRejectAPatternTheLinearEngineCannotRun()
    {
        var service = Build((ProjectA, "Alpha", ChatA, "Chat", ["anything"]));

        // Backreferences need a backtracking engine, and that is exactly what this search refuses
        // to use, so the caller is told rather than left waiting.
        var result = await service.SearchAsync(
            new ChatSearchRequest(@"(a)\1", IsRegex: true), TestContext.Current.CancellationToken);

        result.Error.ShouldNotBeNull();
    }

    [Fact]
    public async Task ShouldRefuseAnEmptyQuery()
    {
        var service = Build((ProjectA, "Alpha", ChatA, "Chat", ["anything"]));

        (await service.SearchAsync(new ChatSearchRequest(string.Empty), TestContext.Current.CancellationToken))
            .Error.ShouldNotBeNull();
    }

    private static ChatSearchService Build(
        params (Guid ProjectId, string ProjectName, Guid ChatId, string ChatTitle, string[] Messages)[] chats) =>
        Build(chats, null);

    private static ChatSearchService Build(
        (Guid ProjectId, string ProjectName, Guid ChatId, string ChatTitle, string[] Messages)[] chats,
        IReadOnlyList<ChatMessageView>? extra)
    {
        var projects = new Mock<IProjectService>(MockBehavior.Strict);
        var service = new Mock<IChatService>(MockBehavior.Strict);
        projects.Setup(item => item.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(chats
            .Select(chat => new ProjectSummary(chat.ProjectId, chat.ProjectName, string.Empty, DateTimeOffset.UnixEpoch, 1, null))
            .DistinctBy(project => project.Id).ToArray());
        foreach (var project in chats.DistinctBy(chat => chat.ProjectId))
            projects.Setup(item => item.GetAsync(project.ProjectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProjectDetails(project.ProjectId, project.ProjectName, string.Empty,
                    DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, [], [], []));
        foreach (var group in chats.GroupBy(chat => chat.ProjectId))
            service.Setup(item => item.ListAsync(group.Key, It.IsAny<CancellationToken>()))
                .ReturnsAsync(group.Select(chat => new ChatSummary(chat.ChatId, chat.ProjectId, chat.ChatTitle,
                    DateTimeOffset.UnixEpoch, 1, DateTimeOffset.UnixEpoch)).ToArray());
        foreach (var chat in chats)
        {
            var messages = chat.Messages
                .Select(content => new ChatMessageView(Guid.NewGuid(), null, "User", content, DateTimeOffset.UnixEpoch))
                .Concat(extra ?? []).ToArray();
            service.Setup(item => item.GetAsync(chat.ProjectId, chat.ChatId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatDetails(chat.ChatId, chat.ProjectId, chat.ChatTitle, DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch, 1, null, messages));
        }

        return new ChatSearchService(projects.Object, service.Object);
    }
}
