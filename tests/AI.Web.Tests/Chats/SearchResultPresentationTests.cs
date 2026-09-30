namespace AI.Web.Tests.ChatSearch;

using AI.Contracts.Chats;
using AI.Web.Chats;
using Shouldly;
using Xunit;

public sealed class SearchResultPresentationTests
{
    private static readonly Guid ChatA = Guid.Parse("019f0000-0000-7000-8000-0000000000a2");
    private static readonly Guid ChatB = Guid.Parse("019f0000-0000-7000-8000-0000000000b2");

    private readonly SearchResultPresentation _presentation = new();

    [Fact]
    public void ShouldGroupByChatWhereItsBestMatchWas()
    {
        var groups = _presentation.Group([Match(ChatB, "b1", 2), Match(ChatA, "a1", 1), Match(ChatB, "b2", 3)]);

        groups.Select(group => group.ChatId).ShouldBe([ChatB, ChatA]);
        groups[0].Matches.Select(match => match.Snippet).ShouldBe(["b1", "b2"]);
        groups[0].Occurrences.ShouldBe(5);
    }

    [Fact]
    public void ShouldMarkEveryOccurrenceIgnoringCase()
    {
        var parts = _presentation.Highlight("Deploy and redeploy", " deploy ");

        parts.ShouldBe([
            new SnippetPart("Deploy", true),
            new SnippetPart(" and re", false),
            new SnippetPart("deploy", true)
        ]);
    }

    [Fact]
    public void ShouldLeaveTextWithoutTheQueryWhole() =>
        _presentation.Highlight("nothing here", "deploy").ShouldBe([new SnippetPart("nothing here", false)]);

    private static ChatSearchMatch Match(Guid chatId, string snippet, int count) =>
        new(Guid.Empty, "Project", chatId, "Chat", Guid.NewGuid(), null, "User", DateTimeOffset.UnixEpoch, snippet, count);
}
