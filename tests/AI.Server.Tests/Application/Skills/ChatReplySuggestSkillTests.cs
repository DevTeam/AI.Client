namespace AI.Application.Tests.Skills;

using System.Runtime.CompilerServices;
using System.Text.Json;
using AI.Application.Chat;
using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Runs;
using AI.Application.Settings;
using AI.Application.Skills;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

public class ChatReplySuggestSkillTests
{
    private readonly Guid _projectId = Guid.CreateVersion7();
    private readonly Guid _chatId = Guid.CreateVersion7();
    private readonly Guid _connectionId = Guid.CreateVersion7();
    private readonly Guid _questionId = Guid.CreateVersion7();
    private readonly Guid _answerId = Guid.CreateVersion7();
    private readonly List<ChatCompletionRequest> _requests = [];

    [Fact]
    public async Task ShouldDraftReplyToTheLastAnswerOfTheBranch()
    {
        var runner = CreateRunner("Yes, add the tests and run them.");

        var result = await runner.RunAsync(Invocation(_answerId), CancellationToken.None);

        result.Status.ShouldBe("Completed");
        result.Output!.Value.GetProperty("text").GetString().ShouldBe("Yes, add the tests and run them.");
        result.Output.Value.GetProperty("message_id").GetGuid().ShouldBe(_answerId);
        var prompt = _requests.Single().ContextMessages![^1].Content;
        prompt.ShouldContain("Fix the empty list case");
        prompt.ShouldContain("Shall I add tests?");
        _requests.Single().Tools.ShouldBeNull();
    }

    [Theory]
    [InlineData("NONE")]
    [InlineData("  none. ")]
    [InlineData("")]
    public async Task ShouldSkipWhenTheModelHasNothingToSuggest(string answer)
    {
        var runner = CreateRunner(answer);

        var result = await runner.RunAsync(Invocation(_answerId), CancellationToken.None);

        result.Status.ShouldBe("Skipped");
        result.Output.ShouldBeNull();
    }

    [Fact]
    public async Task ShouldNotCallTheModelOnceTheBranchHasMovedOn()
    {
        var runner = CreateRunner("unused");

        var result = await runner.RunAsync(Invocation(Guid.CreateVersion7()), CancellationToken.None);

        result.Status.ShouldBe("Skipped");
        _requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ShouldServeTheDraftOnlyForTheAnswerItWasWrittenFor()
    {
        await using var suggestions = new ChatReplySuggestions(CreateRunner("Run them."));

        suggestions.Start(_projectId, _chatId, _chatId, _answerId);
        var draft = await suggestions.GetAsync(_projectId, _chatId, _chatId, _answerId, false, CancellationToken.None);
        var stale = await suggestions.GetAsync(_projectId, _chatId, _chatId, Guid.CreateVersion7(), false, CancellationToken.None);
        var otherProject = await suggestions.GetAsync(Guid.CreateVersion7(), _chatId, _chatId, _answerId, false, CancellationToken.None);
        var again = await suggestions.GetAsync(_projectId, _chatId, _chatId, _answerId, true, CancellationToken.None);

        draft.ShouldNotBeNull().Text.ShouldBe("Run them.");
        stale.ShouldBeNull();
        otherProject.ShouldBeNull();
        again!.Text.ShouldBe("Run them.");
        // The finished draft is reused: asking again writes nothing new.
        _requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ShouldWriteNothingUntilAskedWhenNoDraftWasStarted()
    {
        await using var suggestions = new ChatReplySuggestions(CreateRunner("Run them."));

        var waited = await suggestions.GetAsync(_projectId, _chatId, _chatId, _answerId, false, CancellationToken.None);
        _requests.ShouldBeEmpty();
        var asked = await suggestions.GetAsync(_projectId, _chatId, _chatId, _answerId, true, CancellationToken.None);

        waited.ShouldBeNull();
        asked!.Text.ShouldBe("Run them.");
    }

    private SkillRunner CreateRunner(string answer)
    {
        var now = DateTimeOffset.UtcNow;
        var chat = new ChatDetails(_chatId, _projectId, "Fix", now, now, 3, _connectionId,
            [new ChatMessageView(_questionId, null, "User", "Fix the empty list case", now),
             new ChatMessageView(_answerId, _questionId, "Assistant", "Fixed it. Shall I add tests?", now)],
            [new ChatBranchView(_chatId, _answerId, "Main")]);
        var chats = new Mock<IChatService>(MockBehavior.Strict);
        chats.Setup(item => item.GetAsync(_projectId, _chatId, It.IsAny<CancellationToken>())).ReturnsAsync(chat);
        var projects = new Mock<IProjectService>(MockBehavior.Strict);
        projects.Setup(item => item.GetAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync((ProjectDetails?)null);
        var settings = new Mock<IGlobalSettingsRepository>(MockBehavior.Strict);
        settings.Setup(item => item.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalSettings([new ConnectionSettings(_connectionId, "Main", "https://example.test/v1",
                "test-model", true, true, false)], [], []));
        var secrets = new Mock<IGlobalSecretStore>(MockBehavior.Strict);
        secrets.Setup(item => item.GetAsync("connection", _connectionId, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        var completion = new Mock<IChatCompletionClient>(MockBehavior.Strict);
        completion.Setup(item => item.StreamAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Returns((ChatCompletionRequest request, CancellationToken token) => Respond(request, answer, token));
        var catalog = new BuiltInSkillCatalog();
        var skill = new ChatReplySuggestSkill(catalog, chats.Object, projects.Object, settings.Object, secrets.Object,
            completion.Object, NullLogger<ChatReplySuggestSkill>.Instance);
        return new SkillRunner(catalog, null!, chatReplySuggestSkill: skill);
    }

    private SkillInvocation Invocation(Guid messageId) => new("chat-reply-suggest", _projectId,
        JsonSerializer.SerializeToElement(new { chat_id = _chatId, branch_id = _chatId, message_id = messageId }));

    private async IAsyncEnumerable<ChatCompletionChunk> Respond(ChatCompletionRequest request, string answer,
        [EnumeratorCancellation] CancellationToken token)
    {
        await Task.Yield();
        token.ThrowIfCancellationRequested();
        lock (_requests) _requests.Add(request);
        yield return new ChatCompletionChunk(answer);
    }
}
