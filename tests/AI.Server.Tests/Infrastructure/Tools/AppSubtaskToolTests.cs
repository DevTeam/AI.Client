namespace AI.Infrastructure.Tests.Tools;

using AI.Application.Chat;
using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Settings;
using AI.Application.Tools;
using AI.Application.Workspace;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Settings;
using AI.Contracts.Tools;
using AI.Infrastructure.Projects;
using AI.Infrastructure.Chat;
using AI.Infrastructure.Settings;
using AI.Infrastructure.Storage;
using AI.Infrastructure.Tests.Storage;
using AI.Infrastructure.Tools;
using AI.Infrastructure.Workspace;
using AI.Mcp.App;
using AI.Server.Hosting;
using Moq;
using Shouldly;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

/// <summary>
/// Drives the subtask tool over a real MCP session, because the split it exists for — answer to the
/// model, transcript to the user — only holds if the protocol layer carries it that way.
/// </summary>
public sealed class AppSubtaskToolTests
{
    [Fact]
    public async Task ShouldReturnOnlyTheAnswerToTheModelAndTheTranscriptToTheHost()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        fixture.Completion.Answer("the subtask's conclusion");
        await using var session = await fixture.OpenAsync();
        var tool = session.Tools.Single(item => item.OriginalName == "spawn_subtask");

        var result = await session.CallAsync(tool, fixture.Arguments("summarise the thing"), null,
            TestContext.Current.CancellationToken);

        result.IsError.ShouldBeFalse();
        var outcome = result.StructuredContent!.Value.GetProperty("results")[0];
        outcome.GetProperty("answer").GetString().ShouldBe("the subtask's conclusion");
        outcome.GetProperty("completed").GetBoolean().ShouldBeTrue();

        // The transcript rides in _meta, which the model-facing projection excludes by contract.
        result.Meta.ShouldNotBeNull();
        result.Meta!.Value.GetProperty("transcript").GetArrayLength().ShouldBeGreaterThan(0);
        result.ModelContent.ShouldContain("the subtask's conclusion");
        result.ModelContent.ShouldNotContain("transcript");
    }

    [Fact]
    public async Task ShouldRunSeveralTasksInOneCall()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        fixture.Completion.Answer("done");
        await using var session = await fixture.OpenAsync();
        var tool = session.Tools.Single(item => item.OriginalName == "spawn_subtask");

        var result = await session.CallAsync(tool, fixture.Arguments("first", "second", "third"), null,
            TestContext.Current.CancellationToken);

        var outcomes = result.StructuredContent!.Value.GetProperty("results");
        outcomes.GetArrayLength().ShouldBe(3);
        outcomes.EnumerateArray().Select(item => item.GetProperty("task").GetString())
            .ShouldBe(["first", "second", "third"], ignoreOrder: true);
    }

    [Fact]
    public async Task ShouldRefuseMoreTasksThanItWillRunAtOnce()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        fixture.Completion.Answer("done");
        await using var session = await fixture.OpenAsync();
        var tool = session.Tools.Single(item => item.OriginalName == "spawn_subtask");

        var result = await session.CallAsync(tool,
            fixture.Arguments("a", "b", "c", "d", "e", "f", "g", "h", "i"), null,
            TestContext.Current.CancellationToken);

        result.IsError.ShouldBeTrue();
        result.StructuredContent!.Value.GetProperty("error").GetString().ShouldNotBeNull().ShouldContain("At most");
    }

    [Fact]
    public async Task ShouldReportAFailedSubtaskWithoutLosingItsSiblings()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        // An empty reply is what the agent treats as a broken turn, so one task fails on purpose.
        fixture.Completion.AnswerPerTask(task => task == "broken" ? string.Empty : "fine");
        await using var session = await fixture.OpenAsync();
        var tool = session.Tools.Single(item => item.OriginalName == "spawn_subtask");

        var result = await session.CallAsync(tool, fixture.Arguments("broken", "healthy"), null,
            TestContext.Current.CancellationToken);

        var outcomes = result.StructuredContent!.Value.GetProperty("results").EnumerateArray().ToArray();
        outcomes.Single(item => item.GetProperty("task").GetString() == "broken")
            .GetProperty("error").GetString().ShouldNotBeNull();
        outcomes.Single(item => item.GetProperty("task").GetString() == "healthy")
            .GetProperty("answer").GetString().ShouldBe("fine");
    }

    [Fact]
    public async Task ShouldRefuseWhenTheCallingChatHasNoConnection()
    {
        await using var fixture = await SubtaskFixture.CreateAsync(withConnection: false);
        await using var session = await fixture.OpenAsync();
        var tool = session.Tools.Single(item => item.OriginalName == "spawn_subtask");

        var result = await session.CallAsync(tool, fixture.Arguments("anything"), null, TestContext.Current.CancellationToken);

        result.IsError.ShouldBeTrue();
        result.StructuredContent!.Value.GetProperty("error").GetString().ShouldNotBeNull().ShouldContain("connection");
    }

    [Fact]
    public async Task ShouldReportWhatEachSubtaskIsDoingWhileItRuns()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        fixture.Completion.Answer("done");
        await using var session = await fixture.OpenAsync();
        var tool = session.Tools.Single(item => item.OriginalName == "spawn_subtask");
        var reports = new List<ToolProgress>();

        await session.CallAsync(tool, fixture.Arguments("first", "second"),
            new Progress<ToolProgress>(value => { lock (reports) reports.Add(value); }),
            TestContext.Current.CancellationToken);

        // The caller's live row is fed from these, so a subtask that reports nothing is
        // indistinguishable from one that has hung.
        lock (reports)
        {
            reports.ShouldNotBeEmpty();
            reports.Select(value => value.Message).OfType<string>()
                .ShouldContain(message => message.Contains("1:", StringComparison.Ordinal));
            reports[^1].Total.ShouldBe(2);
            reports.ShouldContain(value => value.Progress == 2);
        }
    }

    [Fact]
    public async Task ShouldRecordToolResultsAsDescribedRatherThanRawProtocolJson()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        fixture.Completion.Answer("done");
        await using var session = await fixture.OpenAsync();
        var tool = session.Tools.Single(item => item.OriginalName == "spawn_subtask");

        var result = await session.CallAsync(tool, fixture.Arguments("do something"), null,
            TestContext.Current.CancellationToken);

        // Storing the protocol's own JSON verbatim nests escaped JSON inside escaped JSON, which is
        // complete and unreadable; the transcript exists to be read.
        var transcript = result.Meta!.Value.GetProperty("transcript").GetRawText();
        transcript.ShouldNotContain("\\\"isError\\\"");
    }

    [Fact]
    public async Task ShouldInheritTheCallingChatsConnectionByDefault()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        fixture.Completion.Answer("done");
        await using var session = await fixture.OpenAsync();
        var tool = session.Tools.Single(item => item.OriginalName == "spawn_subtask");

        var result = await session.CallAsync(tool, fixture.Arguments("anything"), null, TestContext.Current.CancellationToken);

        result.StructuredContent!.Value.GetProperty("results")[0].GetProperty("connection").GetString().ShouldBe("Test");
    }

    [Fact]
    public async Task ShouldAnswerThroughTheConnectionItWasGiven()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        fixture.Completion.Answer("done");
        await using var session = await fixture.OpenAsync();
        var tool = session.Tools.Single(item => item.OriginalName == "spawn_subtask");

        // Handing mechanical work to a cheaper model is the point of naming one.
        var result = await session.CallAsync(tool, fixture.ArgumentsOn(fixture.CheapConnectionId, "anything"), null,
            TestContext.Current.CancellationToken);

        result.IsError.ShouldBeFalse();
        result.StructuredContent!.Value.GetProperty("results")[0].GetProperty("connection").GetString().ShouldBe("Cheap");
    }

    [Fact]
    public async Task ShouldSendUnaddressedWorkToTheConnectionMarkedForSubtasks()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        await fixture.MarkForSubtasksAsync(fixture.CheapConnectionId);
        fixture.Completion.Answer("done");
        await using var session = await fixture.OpenAsync();
        var tool = session.Tools.Single(item => item.OriginalName == "spawn_subtask");

        // The mark exists so that delegated work need not cost what the conversation costs: nothing
        // named a connection here, and the chat's own is the expensive one.
        var result = await session.CallAsync(tool, fixture.Arguments("anything"), null, TestContext.Current.CancellationToken);

        result.StructuredContent!.Value.GetProperty("results")[0].GetProperty("connection").GetString().ShouldBe("Cheap");
    }

    [Fact]
    public async Task ShouldDealUnaddressedWorkOutOverEveryMarkedConnection()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        await fixture.MarkForSubtasksAsync(fixture.DefaultConnectionId, fixture.CheapConnectionId);
        fixture.Completion.Answer("done");
        await using var session = await fixture.OpenAsync();
        var tool = session.Tools.Single(item => item.OriginalName == "spawn_subtask");

        // Marking several is how a fan-out stops queueing behind one provider: nothing here named a
        // connection, so the tasks are dealt out over the marked ones in turn.
        var result = await session.CallAsync(tool, fixture.Arguments("one", "two", "three"), null,
            TestContext.Current.CancellationToken);

        var used = result.StructuredContent!.Value.GetProperty("results").EnumerateArray()
            .Select(item => item.GetProperty("connection").GetString()).ToArray();
        used.ShouldBe(["Test", "Cheap", "Test"]);
    }

    [Fact]
    public async Task ShouldStillObeyANamedConnectionOverTheMark()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        await fixture.MarkForSubtasksAsync(fixture.CheapConnectionId);
        fixture.Completion.Answer("done");
        await using var session = await fixture.OpenAsync();
        var tool = session.Tools.Single(item => item.OriginalName == "spawn_subtask");

        // The mark is a fallback, not a wall: checking every connection has to stay possible.
        var result = await session.CallAsync(tool, fixture.ArgumentsOn(fixture.DefaultConnectionId, "anything"), null,
            TestContext.Current.CancellationToken);

        result.StructuredContent!.Value.GetProperty("results")[0].GetProperty("connection").GetString().ShouldBe("Test");
    }

    [Fact]
    public async Task ShouldLetEachTaskNameItsOwnConnection()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        fixture.Completion.Answer("done");
        await using var session = await fixture.OpenAsync();
        var tool = session.Tools.Single(item => item.OriginalName == "spawn_subtask");

        // Checking a set of connections is the case this exists for: separate calls would run one
        // after another, while the tasks of one call run together.
        var result = await session.CallAsync(tool, fixture.ArgumentsPerTask(
            ("first", fixture.DefaultConnectionId), ("second", fixture.CheapConnectionId)), null,
            TestContext.Current.CancellationToken);

        result.IsError.ShouldBeFalse();
        result.StructuredContent!.Value.GetProperty("results").EnumerateArray()
            .Select(item => item.GetProperty("connection").GetString())
            .ShouldBe(["Test", "Cheap"], ignoreOrder: true);
    }

    [Fact]
    public async Task ShouldRejectTheWholeCallBeforeRunningAnythingWhenOneTasksConnectionIsBad()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        fixture.Completion.Answer("done");
        await using var session = await fixture.OpenAsync();
        var tool = session.Tools.Single(item => item.OriginalName == "spawn_subtask");

        var result = await session.CallAsync(tool, fixture.ArgumentsPerTask(
            ("fine", fixture.DefaultConnectionId), ("doomed", fixture.OffConnectionId)), null,
            TestContext.Current.CancellationToken);

        // Spending a minute on the healthy tasks before failing on the last one's endpoint would be
        // the worst of both: every connection is resolved before any work starts.
        result.IsError.ShouldBeTrue();
        result.StructuredContent!.Value.GetProperty("results").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task ShouldRefuseAConnectionThatIsUnknownOrSwitchedOff()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        fixture.Completion.Answer("done");
        await using var session = await fixture.OpenAsync();
        var tool = session.Tools.Single(item => item.OriginalName == "spawn_subtask");

        foreach (var rejected in new[] { Guid.NewGuid(), fixture.OffConnectionId })
        {
            var result = await session.CallAsync(tool, fixture.ArgumentsOn(rejected, "anything"), null,
                TestContext.Current.CancellationToken);

            // A subtask picks among the connections the user configured; it does not describe one.
            result.IsError.ShouldBeTrue();
            result.StructuredContent!.Value.GetProperty("error").GetString()
                .ShouldNotBeNull().ShouldContain("enabled connection");
        }
    }

    [Fact]
    public async Task ShouldLeaveNothingBehindOnDisk()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        fixture.Completion.Answer("done");
        var before = fixture.FileCount;
        await using var session = await fixture.OpenAsync();
        var tool = session.Tools.Single(item => item.OriginalName == "spawn_subtask");

        await session.CallAsync(tool, fixture.Arguments("do something"), null, TestContext.Current.CancellationToken);

        // The whole point of a subtask is that it is not a chat: no manifest, no message nodes, no
        // run state — nothing to find afterwards and nothing to clean up.
        fixture.FileCount.ShouldBe(before);
        (await fixture.Chats.ListAsync(fixture.ProjectId, CancellationToken.None)).Count.ShouldBe(1);
    }

    private sealed class SubtaskFixture : IAsyncDisposable
    {
        private readonly MemoryFileSystem _fileSystem = new();
        private readonly SubtaskComposition _composition;

        public StubCompletion Completion { get; } = new();
        public IProjectService Projects => _composition.Resolve<IProjectService>();
        public IChatService Chats => _composition.Resolve<IChatService>();
        private IGlobalSettingsRepository Settings => _composition.Resolve<IGlobalSettingsRepository>();
        private AppToolSessionFactory Sessions => _composition.Resolve<AppToolSessionFactory>();
        public Guid ProjectId { get; private set; }
        public Guid ChatId { get; private set; }
        public Guid DefaultConnectionId { get; } = Guid.NewGuid();
        public Guid CheapConnectionId { get; } = Guid.NewGuid();
        public Guid OffConnectionId { get; } = Guid.NewGuid();
        public int FileCount => _fileSystem.Files.Count;

        private SubtaskFixture()
        {
            // No tool servers: a subtask that needs none is enough to prove the plumbing, and it
            // keeps the test from starting child processes.
            _composition = new SubtaskComposition(
                options: new ServerOptions("data", null, true),
                fileSystem: _fileSystem,
                completion: Completion,
                tools: Mock.Of<IToolSessionFactory>());
        }

        public static async Task<SubtaskFixture> CreateAsync(bool withConnection = true)
        {
            var fixture = new SubtaskFixture();
            await fixture.Settings.SaveAsync(new GlobalSettings(
                withConnection
                    ? [new ConnectionSettings(fixture.DefaultConnectionId, "Test", "https://example.test/v1", "model", true, true, false),
                       new ConnectionSettings(fixture.CheapConnectionId, "Cheap", "https://example.test/v1", "small", true, false, false),
                       new ConnectionSettings(fixture.OffConnectionId, "Off", "https://example.test/v1", "model", false, false, false)]
                    : [],
                [], []), CancellationToken.None);
            fixture.ProjectId = (await fixture.Projects.CreateAsync(new CreateProjectRequest("Test", ""), CancellationToken.None)).Id;
            fixture.ChatId = (await fixture.Chats.CreateAsync(fixture.ProjectId, new CreateChatRequest("Chat"), CancellationToken.None)).Id;
            return fixture;
        }

        /// <summary>Puts the standing mark on connections, the way the settings screen does.</summary>
        public async Task MarkForSubtasksAsync(params Guid[] connectionIds)
        {
            var stored = await Settings.LoadAsync(CancellationToken.None);
            await Settings.SaveAsync(stored with
            {
                Connections = stored.Connections
                    .Select(item => item with { ForSubtasks = connectionIds.Contains(item.Id) }).ToArray()
            }, CancellationToken.None);
        }

        public Task<IToolSession> OpenAsync(bool interactive = true) => Sessions.OpenAsync([],
            new ToolRunContext(ProjectId, ChatId, ChatId, interactive), TestContext.Current.CancellationToken);

        public string Arguments(params string[] tasks) =>
            JsonSerializer.Serialize(new { projectId = ProjectId, chatId = ChatId, tasks = tasks.Select(task => new { task }) });

        public string ArgumentsOn(Guid connectionId, params string[] tasks) =>
            JsonSerializer.Serialize(new { projectId = ProjectId, chatId = ChatId, tasks = tasks.Select(task => new { task }), connectionId });

        /// <summary>One connection per task, which is what lets a set of them be exercised at once.</summary>
        public string ArgumentsPerTask(params (string Task, Guid Connection)[] tasks) =>
            JsonSerializer.Serialize(new
            {
                projectId = ProjectId,
                chatId = ChatId,
                tasks = tasks.Select(item => new { task = item.Task, connectionId = item.Connection }),
            });

        public ValueTask DisposeAsync() => _composition.DisposeAsync();
    }

    /// <summary>Answers immediately, so a subtask completes within the call that started it.</summary>
    private sealed class StubCompletion : IChatCompletionClient
    {
        private Func<string, string> _answer = _ => string.Empty;

        public void Answer(string text) => _answer = _ => text;

        public void AnswerPerTask(Func<string, string> answer) => _answer = answer;

        public Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
            ChatCompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new ChatCompletionChunk(_answer(request.Message));
        }
    }
}
