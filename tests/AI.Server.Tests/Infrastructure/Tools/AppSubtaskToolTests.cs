namespace AI.Infrastructure.Tests.Tools;

using AI.Application.Chat;
using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Settings;
using AI.Application.Tools;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Settings;
using AI.Contracts.Tools;
using AI.Infrastructure.Tests.Storage;
using AI.Mcp.App;
using AI.Server.Hosting;
using Moq;
using Shouldly;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

/// <summary>Real MCP calls exercise asynchronous launch and run-owned collection, with a controlled model.</summary>
[Trait("Category", "Integration")]
public sealed class AppSubtaskToolTests
{
    private const string Evidence = "Result verified.\n<subtask_evidence>{\"checks\":[{\"name\":\"focused check\",\"status\":\"Passed\",\"details\":\"exit code 0\"}],\"unresolved\":[]}</subtask_evidence>";

    private static Task<ToolCallResult> CallAsync(IToolSession session, string name, object arguments,
        CancellationToken token, IProgress<ToolProgress>? progress = null) =>
        session.CallAsync(session.Tools.Single(tool => tool.OriginalName == name), JsonSerializer.Serialize(arguments), progress, token);

    private static async Task<string> SpawnAsync(IToolSession session, string task, Guid? connectionId = null,
        string[]? acceptanceCriteria = null)
    {
        var result = await CallAsync(session, "spawn_subtask", new { task, connectionId, acceptanceCriteria }, TestContext.Current.CancellationToken);
        result.IsError.ShouldBeFalse(result.ModelContent);
        return result.StructuredContent!.Value.GetProperty("taskId").GetString()!;
    }

    private static Task<ToolCallResult> WaitAsync(IToolSession session, params string[] ids) =>
        CallAsync(session, "subtask_wait", new { taskIds = ids, mode = "all", waitTimeoutMs = 10000 }, TestContext.Current.CancellationToken);

    private static JsonElement Outcome(ToolCallResult result, int index = 0) =>
        result.StructuredContent!.Value.GetProperty("tasks")[index].GetProperty("result");

    [Fact]
    public async Task LaunchReturnsBeforeCompletionAndCollectionKeepsTheTranscriptOutOfModelContext()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        fixture.Completion.Answer(Evidence);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Completion.HoldAnswerUntil(gate.Task);
        await using var session = await fixture.OpenAsync();
        var id = await SpawnAsync(session, "Inspect one module");
        await fixture.Completion.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var snapshot = await CallAsync(session, "subtask_wait", new { taskIds = new[] { id }, waitTimeoutMs = 0 }, TestContext.Current.CancellationToken);
        snapshot.StructuredContent!.Value.GetProperty("tasks")[0].GetProperty("status").GetString().ShouldBe("running");
        gate.SetResult();
        var result = await WaitAsync(session, id);
        Outcome(result).GetProperty("completed").GetBoolean().ShouldBeTrue();
        Outcome(result).GetProperty("checks")[0].GetProperty("status").GetString().ShouldBe("Passed");
        result.Meta.ShouldNotBeNull();
        result.Meta!.Value.GetProperty("transcript").GetArrayLength().ShouldBeGreaterThan(0);
        result.ModelContent.ShouldNotContain("transcript");
        var replay = await WaitAsync(session, id);
        Outcome(replay).GetProperty("answer").GetString().ShouldBe("Result verified.");
        fixture.Completion.StreamRequests.ShouldBe(1);
    }

    [Fact]
    public async Task MultipleSingleLaunchesRunTogetherAndUseTheMarkedPoolInOrder()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        await fixture.MarkForSubtasksAsync(fixture.DefaultConnectionId, fixture.CheapConnectionId);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Completion.HoldAnswerUntil(gate.Task);
        fixture.Completion.Answer(Evidence);
        await using var session = await fixture.OpenAsync();
        var first = await SpawnAsync(session, "First module");
        var second = await SpawnAsync(session, "Second module");
        first.ShouldNotBe(second);
        gate.SetResult();
        var result = await WaitAsync(session, first, second);
        Outcome(result, 0).GetProperty("connection").GetString().ShouldBe("Test");
        Outcome(result, 1).GetProperty("connection").GetString().ShouldBe("Cheap");
        fixture.Completion.StreamRequests.ShouldBe(2);
    }

    [Fact]
    public async Task CancelledWaitLeavesWorkAliveAndStopCancelsOnlyTheNamedTask()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Completion.HoldAnswerUntil(gate.Task);
        fixture.Completion.Answer(Evidence);
        await using var session = await fixture.OpenAsync();
        var first = await SpawnAsync(session, "First");
        var second = await SpawnAsync(session, "Second");
        using var waitCancellation = new CancellationTokenSource();
        var waiting = CallAsync(session, "subtask_wait", new { taskIds = new[] { first, second } }, waitCancellation.Token);
        await waitCancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => waiting);
        var stopped = await CallAsync(session, "subtask_stop", new { taskIds = new[] { first } }, TestContext.Current.CancellationToken);
        stopped.StructuredContent!.Value.GetProperty("tasks")[0].GetProperty("status").GetString().ShouldBe("cancelled");
        gate.SetResult();
        Outcome(await WaitAsync(session, second)).GetProperty("completed").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task OtherBranchesCannotReadOrStopAnOwnedHandle()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        fixture.Completion.Answer(Evidence);
        await using var owner = await fixture.OpenAsync();
        var id = await SpawnAsync(owner, "One owned task");
        await using var stranger = await fixture.OpenAsync(branchId: Guid.NewGuid());
        foreach (var tool in new[] { "subtask_wait", "subtask_stop" })
        {
            var rejected = await CallAsync(stranger, tool, tool == "subtask_wait" ? (object)new { taskIds = new[] { id }, waitTimeoutMs = 0 } : new { taskIds = new[] { id } }, TestContext.Current.CancellationToken);
            rejected.IsError.ShouldBeTrue();
        }
        Outcome(await WaitAsync(owner, id)).GetProperty("completed").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task FailedWorkDoesNotCancelItsSibling()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        fixture.Completion.AnswerPerTask(task => task.StartsWith("Fail", StringComparison.Ordinal)
            ? throw new InvalidOperationException("Controlled model failure.") : Evidence);
        await using var session = await fixture.OpenAsync();
        var failed = await SpawnAsync(session, "Fail this task");
        var healthy = await SpawnAsync(session, "Complete this task");
        var result = await WaitAsync(session, failed, healthy);
        result.StructuredContent!.Value.GetProperty("tasks")[0].GetProperty("status").GetString().ShouldBe("failed");
        Outcome(result, 0).GetProperty("error").GetString().ShouldBe("Controlled model failure.");
        Outcome(result, 1).GetProperty("completed").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task ShouldLetATeammateDelegateAtDefaultDepthUsingItsBranchConnection()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        fixture.Completion.Answer(Evidence);
        var token = TestContext.Current.CancellationToken;
        var chat = (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, token)).ShouldNotBeNull();
        var charter = Guid.NewGuid();
        chat = (await fixture.Chats.AppendMessageAsync(fixture.ProjectId, fixture.ChatId,
            new(charter, null, "User", "Team charter", chat.Revision, BranchId: fixture.ChatId), token)).ShouldNotBeNull();
        var branch = Guid.NewGuid();
        (await fixture.Chats.AppendMessageAsync(fixture.ProjectId, fixture.ChatId,
            new(Guid.NewGuid(), charter, "User", "Backend assignment", chat.Revision, BranchId: branch,
                ParentBranchId: fixture.ChatId, BranchMember: new("Ada", "Backend"),
                BranchSettings: new(ConnectionId: fixture.CheapConnectionId)), token)).ShouldNotBeNull();
        await using var session = await fixture.OpenAsync(branchId: branch);
        var id = await SpawnAsync(session, "One backend policy");
        Outcome(await WaitAsync(session, id)).GetProperty("connection").GetString().ShouldBe("Cheap");
        var request = fixture.Completion.Requests.ShouldHaveSingleItem();
        request.Model.ShouldBe("small");
        string.Join("\n", request.ContextMessages!.Select(message => message.Content)).ShouldContain("Do not delegate");
    }

    [Fact]
    public async Task DepthLimitRefusesLaunchWithoutStartingWork()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        await using var session = await fixture.OpenAsync(interactive: false, subtaskDepth: 1);
        var result = await CallAsync(session, "spawn_subtask", new { task = "Nested" }, TestContext.Current.CancellationToken);
        result.IsError.ShouldBeTrue();
        fixture.Completion.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task RaisedDepthAllowsAnotherLevelAndKeepsTheStepBudget()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        await fixture.SetMaxSubtaskDepthAsync(2);
        fixture.Completion.LoopWithUnavailableTools();
        await using var session = await fixture.OpenAsync(interactive: false, subtaskDepth: 1);
        var id = await SpawnAsync(session, "Nested bounded work");
        var result = await WaitAsync(session, id);
        Outcome(result).GetProperty("answer").GetString().ShouldNotBeNull().ShouldContain("step budget");
        fixture.Completion.StreamRequests.ShouldBe(40);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrDisabledConnectionsFailBeforeLaunch(bool disabled)
    {
        await using var fixture = await SubtaskFixture.CreateAsync(withConnection: disabled);
        await using var session = await fixture.OpenAsync();
        var result = await CallAsync(session, "spawn_subtask", new { task = "One", connectionId = disabled ? fixture.OffConnectionId : Guid.NewGuid() }, TestContext.Current.CancellationToken);
        result.IsError.ShouldBeTrue();
        fixture.Completion.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExplicitConnectionWinsAndCriteriaRemainInTheWorkerPrompt()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        await fixture.MarkForSubtasksAsync(fixture.DefaultConnectionId);
        fixture.Completion.Answer(Evidence);
        await using var session = await fixture.OpenAsync();
        var id = await SpawnAsync(session, "Preserve source blocks; translate section A", fixture.CheapConnectionId, ["Output is complete."]);
        var result = await WaitAsync(session, id);
        Outcome(result).GetProperty("connection").GetString().ShouldBe("Cheap");
        var prompt = fixture.Completion.Requests.ShouldHaveSingleItem().Message;
        prompt.ShouldContain("Acceptance criteria:");
        prompt.ShouldContain("Output is complete.");
        prompt.ShouldContain("<subtask_evidence>");
        var schema = session.Tools.Single(tool => tool.OriginalName == "spawn_subtask").ModelDefinition.InputSchema.GetProperty("properties");
        schema.TryGetProperty("tasks", out _).ShouldBeFalse();
        schema.TryGetProperty("sharedInstructions", out _).ShouldBeFalse();
        schema.TryGetProperty("projectId", out _).ShouldBeFalse();
        schema.GetProperty("acceptanceCriteria").GetProperty("maxItems").GetInt32().ShouldBe(32);
    }

    [Fact]
    public async Task WaitForwardsProgressAndMalformedEvidenceRemainsUnresolved()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Completion.HoldAnswerUntil(gate.Task);
        fixture.Completion.Answer("Unverified answer");
        await using var session = await fixture.OpenAsync();
        var id = await SpawnAsync(session, "Inspect");
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = await CallAsync(session, "subtask_wait", new { taskIds = new[] { id }, mode = "all", waitTimeoutMs = 10000 },
            TestContext.Current.CancellationToken, new SynchronousProgress(value =>
            {
                value.Total.ShouldBe(1);
                reported.TrySetResult();
                gate.TrySetResult();
            }));
        await reported.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Outcome(result).GetProperty("unresolved").GetArrayLength().ShouldBeGreaterThan(0);
        result.Meta!.Value.GetProperty("transcript").GetRawText().ShouldNotContain("\\\"isError\\\"");
    }

    [Fact]
    public async Task NoAdditionalChatIsPersisted()
    {
        await using var fixture = await SubtaskFixture.CreateAsync();
        fixture.Completion.Answer(Evidence);
        var before = fixture.FileCount;
        await using var session = await fixture.OpenAsync();
        await WaitAsync(session, await SpawnAsync(session, "Inspect"));
        fixture.FileCount.ShouldBe(before);
        (await fixture.Chats.ListAsync(fixture.ProjectId, CancellationToken.None)).Count.ShouldBe(1);
    }

    private sealed class SubtaskFixture : IAsyncDisposable
    {
        private readonly MemoryFileSystem _fileSystem = new();
        private readonly SubtaskComposition _composition;

        public StubCompletion Completion { get; } = new();
        public IProjectService Projects => _composition.Projects;
        public IChatService Chats => _composition.Chats;
        private IGlobalSettingsRepository Settings => _composition.Settings;
        private IMcpServerConnection Sessions => _composition.Sessions;
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

        public async Task SetChatConnectionAsync(Guid connectionId)
        {
            var chat = await Chats.GetAsync(ProjectId, ChatId, CancellationToken.None) ?? throw new InvalidOperationException();
            await Chats.UpdateEndpointAsync(ProjectId, ChatId, new UpdateChatEndpointRequest(connectionId, chat.Revision),
                CancellationToken.None);
        }

        public async Task<IToolSession> OpenAsync(bool interactive = true, int subtaskDepth = 0, Guid? branchId = null)
        {
            var run = new ToolRunContext(ProjectId, ChatId, branchId ?? ChatId, interactive,
                IsSubtask: subtaskDepth > 0, SubtaskDepth: subtaskDepth);
            var scope = _composition.Subtasks.Begin(run, TestContext.Current.CancellationToken);
            try { return new OwnedSession(await Sessions.OpenAsync([], run, TestContext.Current.CancellationToken), scope); }
            catch { await scope.DisposeAsync(); throw; }
        }

        private sealed class OwnedSession(IToolSession inner, IAsyncDisposable scope) : IToolSession
        {
            public IReadOnlyList<AgentTool> Tools => inner.Tools;
            public string ValidateArguments(AgentTool tool, string arguments) => inner.ValidateArguments(tool, arguments);
            public Task<ToolCallResult> CallAsync(AgentTool tool, string arguments, IProgress<ToolProgress>? progress, CancellationToken token) =>
                inner.CallAsync(tool, arguments, progress, token);
            public async ValueTask DisposeAsync() { await scope.DisposeAsync(); await inner.DisposeAsync(); }
        }

        public async Task SetMaxSubtaskDepthAsync(int depth)
        {
            var stored = await Settings.LoadAsync(CancellationToken.None);
            await Settings.SaveAsync(stored with { ChatAutomation = new ChatAutomationSettings(MaxSubtaskDepth: depth) },
                CancellationToken.None);
        }

        public ValueTask DisposeAsync() => _composition.DisposeAsync();
    }

    /// <summary>A controllable model for launch, completion and cancellation races.</summary>
    private sealed class StubCompletion : IChatCompletionClient
    {
        private Func<string, string> _answer = _ => string.Empty;
        private Task _gate = Task.CompletedTask;
        private bool _loop;
        private int _call;
        private int _streamRequests;

        public List<ChatCompletionRequest> Requests { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StreamRequests => Volatile.Read(ref _streamRequests);

        public void Answer(string text) => _answer = _ => text;

        public void AnswerPerTask(Func<string, string> answer) => _answer = answer;

        /// <summary>Withholds every answer until the gate completes, bounded so a broken gate fails a test.</summary>
        public void HoldAnswerUntil(Task gate) => _gate = gate;

        public void LoopWithUnavailableTools() => _loop = true;

        public Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ChatCompletionResponse("Stopped at the model-step budget with unfinished work.", request.Model));

        public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
            ChatCompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            lock (Requests) Requests.Add(request);
            Started.TrySetResult();
            await _gate.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            await Task.Yield();
            Interlocked.Increment(ref _streamRequests);
            if (_loop)
            {
                var call = Interlocked.Increment(ref _call);
                yield return new ChatCompletionChunk(string.Empty,
                    ToolCalls: [new($"call-{call}", "mcp_built_in__read_text_file", $"{{\"path\":\"missing-{call}\"}}")]);
                yield break;
            }
            yield return new ChatCompletionChunk(_answer(request.Message));
        }
    }

    private sealed class SynchronousProgress(Action<ToolProgress> report) : IProgress<ToolProgress>
    {
        public void Report(ToolProgress value) => report(value);
    }
}
