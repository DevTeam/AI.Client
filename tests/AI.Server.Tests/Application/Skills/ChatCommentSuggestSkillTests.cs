namespace AI.Application.Tests.Skills;

using System.Runtime.CompilerServices;
using System.Text.Json;
using AI.Application.Chat;
using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Resources;
using AI.Application.Settings;
using AI.Application.Skills;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Resources;
using AI.Contracts.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

public class ChatCommentSuggestSkillTests
{
    private readonly Guid _projectId = Guid.CreateVersion7();
    private readonly Guid _chatId = Guid.CreateVersion7();
    private readonly Guid _connectionId = Guid.CreateVersion7();
    private readonly Guid _answerId = Guid.CreateVersion7();
    private readonly List<ChatCompletionRequest> _requests = [];
    private ChatAutomationSettings _automation = new();

    [Fact]
    public async Task ShouldDraftCommentOnTheSelectedTextOfAMessage()
    {
        var suggestions = CreateSuggestions("Why skip the empty list here?");

        var draft = await suggestions.SuggestAsync(_projectId, _chatId,
            new ReviewCommentSuggestionRequest("return items[0];", _answerId), CancellationToken.None);

        draft.ShouldNotBeNull().Text.ShouldBe("Why skip the empty list here?");
        var prompt = _requests.Single().ContextMessages![^1].Content;
        prompt.ShouldContain("return items[0];");
        prompt.ShouldContain("I changed First to read the list");
        _requests.Single().Tools.ShouldBeNull();
    }

    [Fact]
    public async Task ShouldReadTheDiffAroundCommentedLines()
    {
        var suggestions = CreateSuggestions("This throws on an empty list.");

        var draft = await suggestions.SuggestAsync(_projectId, _chatId,
            new ReviewCommentSuggestionRequest("return items[0];", Path: "src/List.cs", Diff: "@@ -1 +1 @@\n-return null;\n+return items[0];"),
            CancellationToken.None);

        draft!.Text.ShouldBe("This throws on an empty list.");
        var prompt = _requests.Single().ContextMessages![^1].Content;
        prompt.ShouldContain("src/List.cs");
        prompt.ShouldContain("-return null;");
    }

    [Theory]
    [InlineData("NONE")]
    [InlineData(" none. ")]
    [InlineData("")]
    public async Task ShouldOfferNothingWhenTheModelHasNoComment(string answer)
    {
        var suggestions = CreateSuggestions(answer);

        var draft = await suggestions.SuggestAsync(_projectId, _chatId,
            new ReviewCommentSuggestionRequest("return items[0];", _answerId), CancellationToken.None);

        draft.ShouldBeNull();
    }

    [Fact]
    public async Task ShouldDraftUnaskedOnlyWhileTheSettingAllowsIt()
    {
        _automation = new ChatAutomationSettings(SuggestComments: false);
        var suggestions = CreateSuggestions("Why skip the empty list here?");

        var automatic = await suggestions.SuggestAsync(_projectId, _chatId,
            new ReviewCommentSuggestionRequest("return items[0];", _answerId, Automatic: true), CancellationToken.None);
        _requests.ShouldBeEmpty();
        var asked = await suggestions.SuggestAsync(_projectId, _chatId,
            new ReviewCommentSuggestionRequest("return items[0];", _answerId), CancellationToken.None);

        automatic.ShouldBeNull();
        asked!.Text.ShouldBe("Why skip the empty list here?");
    }

    private ReviewCommentSuggestions CreateSuggestions(string answer)
    {
        var now = DateTimeOffset.UtcNow;
        var questionId = Guid.CreateVersion7();
        var chat = new ChatDetails(_chatId, _projectId, "Fix", now, now, 3, _connectionId,
            [new ChatMessageView(questionId, null, "User", "Fix the empty list case", now),
             new ChatMessageView(_answerId, questionId, "Assistant", "I changed First to read the list:\nreturn items[0];", now)],
            [new ChatBranchView(_chatId, _answerId, "Main")]);
        var chats = new Mock<IChatService>(MockBehavior.Strict);
        chats.Setup(item => item.GetAsync(_projectId, _chatId, It.IsAny<CancellationToken>())).ReturnsAsync(chat);
        var projects = new Mock<IProjectService>(MockBehavior.Strict);
        projects.Setup(item => item.GetAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync((ProjectDetails?)null);
        var settings = new Mock<IGlobalSettingsRepository>(MockBehavior.Strict);
        settings.Setup(item => item.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new GlobalSettings([new ConnectionSettings(_connectionId, "Main", "https://example.test/v1",
                "test-model", true, true, false)], [], [], _automation));
        var secrets = new Mock<IGlobalSecretStore>(MockBehavior.Strict);
        secrets.Setup(item => item.GetAsync("connection", _connectionId, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        var completion = new Mock<IChatCompletionClient>(MockBehavior.Strict);
        completion.Setup(item => item.StreamAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Returns((ChatCompletionRequest request, CancellationToken token) => Respond(request, answer, token));
        var catalog = new BuiltInSkillCatalog();
        var skill = new ChatCommentSuggestSkill(catalog, chats.Object, projects.Object, settings.Object, secrets.Object,
            completion.Object, NullLogger<ChatCommentSuggestSkill>.Instance);
        return new ReviewCommentSuggestions(new SkillRunner(catalog, null!, chatCommentSuggestSkill: skill), settings.Object);
    }

    private async IAsyncEnumerable<ChatCompletionChunk> Respond(ChatCompletionRequest request, string answer,
        [EnumeratorCancellation] CancellationToken token)
    {
        await Task.Yield();
        token.ThrowIfCancellationRequested();
        lock (_requests) _requests.Add(request);
        yield return new ChatCompletionChunk(answer);
    }
}
