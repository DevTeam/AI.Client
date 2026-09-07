namespace AI.Client.Infrastructure.Tests.Storage;

using AI.Client.Application.Chat;
using Application.Chats;
using AI.Client.Application.Projects;
using Application.Runs;
using AI.Client.Application.Settings;
using AI.Client.Contracts.Chat;
using Contracts.Chats;
using AI.Client.Contracts.Projects;
using Contracts.Runs;
using AI.Client.Contracts.Settings;
using Projects;
using Settings;
using AI.Client.Infrastructure.Storage;
using Moq;
using Shouldly;
using Xunit;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AI.Client.Application.Tools;
using System.Text.Json;

public sealed class ChatExecutionTests
{
    [Theory]
    [InlineData("Allow", 1)]
    [InlineData("Deny", 0)]
    public async Task ShouldRespectPoliciesWithoutPrompting(string decision, int expectedCalls)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetPolicyAsync(decision);
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Run command"));
        var first = await fixture.NextCallAsync();
        first.Request.Tools!.Count.ShouldBe(decision == "Deny" ? 0 : 1);
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_default__process_run", "{}")];
        first.Answer.SetResult("");
        var second = await fixture.NextCallAsync();
        fixture.Tools.CallCount.ShouldBe(expectedCalls);
        second.Answer.SetResult("Done");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
    }

    [Fact]
    public async Task RevokedPolicyWhileWaitingForApprovalMustPreventExecution()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetPolicyAsync("Ask");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Run command"));
        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_default__process_run", "{}")];
        first.Answer.SetResult("");
        var pending = await fixture.WaitAsync(run => run.PendingApproval is not null);
        await fixture.SetPolicyAsync("Deny");
        await fixture.Dispatcher.DecideToolAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId,
            new ToolApprovalDecision(pending.PendingApproval!.Id, true), CancellationToken.None);
        var second = await fixture.NextCallAsync();
        fixture.Tools.CallCount.ShouldBe(0);
        second.Answer.SetResult("Denied");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ToolMustWaitForApprovalAndPersistTheExchange(bool allow)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetPolicyAsync("Ask");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Run command"));
        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_default__process_run", "{}")];
        first.Answer.SetResult("");
        var pending = await fixture.WaitAsync(run => run.PendingApproval is not null);
        fixture.Tools.CallCount.ShouldBe(0);
        var before = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        before!.Messages.Single(message => message.ToolCalls is not null).ToolCalls![0].Id.ShouldBe("call-1");
        (await fixture.Dispatcher.DecideToolAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId,
            new ToolApprovalDecision(Guid.NewGuid(), true), CancellationToken.None)).ShouldBeFalse();
        (await fixture.Dispatcher.DecideToolAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId,
            new ToolApprovalDecision(pending.PendingApproval!.Id, allow), CancellationToken.None)).ShouldBeTrue();
        var second = await fixture.NextCallAsync();
        second.Request.ContextMessages![^1].ToolCallId.ShouldBe("call-1");
        fixture.Tools.CallCount.ShouldBe(allow ? 1 : 0);
        second.Answer.SetResult("Final response");
        var completed = await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        await fixture.RestartAsync();
        var restored = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        restored!.Messages.Single(message => message.Id == completed.HeadMessageId).Content.ShouldBe("Final response");
        ChatContext.Get(restored, completed.HeadMessageId!.Value).Select(message => message.Role).ShouldBe(["user", "assistant", "tool", "assistant"]);
        fixture.Tools.CallCount.ShouldBe(allow ? 1 : 0);
    }

    [Fact]
    public async Task StoppedApprovalMustNotExecuteOnRestart()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetPolicyAsync("Ask");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Run command"));
        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_default__process_run", "{}")];
        first.Answer.SetResult("");
        await fixture.WaitAsync(run => run.PendingApproval is not null);
        await fixture.Dispatcher.StopAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None);
        await fixture.RestartAsync();
        await fixture.Dispatcher.ResumeAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None);
        var resumed = await fixture.NextCallAsync();
        fixture.Tools.CallCount.ShouldBe(0);
        resumed.Request.ContextMessages![^1].Content.ShouldContain("interrupted", Case.Insensitive);
        resumed.Answer.SetResult("Stopped");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
    }
    [Fact]
    public async Task QueueShouldRemainPausedAndRepeatedSubmitShouldHaveNoSecondEffect()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Queued", ChatSubmitMode.Queue);
        var first = await fixture.SubmitAsync(request);
        var second = await fixture.SubmitAsync(request);
        second.ShouldBe(first);
        second.Status.ShouldBe(ChatRunStatus.Paused);
        second.Queue.ShouldHaveSingleItem();
        fixture.Completion.Calls.Reader.TryRead(out _).ShouldBeFalse();
        await fixture.Dispatcher.ResumeAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None);
        var call = await fixture.NextCallAsync();
        call.Request.Tools.ShouldBeEmpty();
        fixture.Tools.OpenCount.ShouldBe(0);
        call.Answer.SetResult("Reply");
        var completed = await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        completed.Queue.ShouldBeEmpty();
        completed.ChatRevision.ShouldBeGreaterThan(first.ChatRevision);
        completed.HeadMessageId.ShouldNotBeNull();
    }

    [Fact]
    public async Task ForkShouldUseOnlyItsAncestorsAndKeepTheMainHead()
    {
        await using var fixture = await Fixture.CreateAsync();
        var message = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), message, "Original"));
        (await fixture.NextCallAsync()).Answer.SetResult("Original reply");
        var main = await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        var forkId = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), forkId, "Alternative", ChatSubmitMode.Fork, ParentMessageId: message));
        var fork = await fixture.NextCallAsync();
        fork.Request.ContextMessages!.Select(item => item.Content).ShouldBe(["Original", "Alternative"]);
        fork.Answer.SetResult("Alternative reply");
        await fixture.WaitAsync(run => run.BranchId == forkId && run.Status == ChatRunStatus.Completed);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Branches!.Single(branch => branch.Id == fixture.ChatId).HeadMessageId.ShouldBe(main.HeadMessageId);
    }

    [Fact]
    public async Task ConcurrentBranchesShouldNotShareTheirReplyParent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var root = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), root, "Root"));
        var main = await fixture.NextCallAsync();
        var forkId = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), forkId, "Fork", ChatSubmitMode.Fork, ParentMessageId: root));
        var fork = await fixture.NextCallAsync();
        fork.Answer.SetResult("Fork reply");
        await fixture.WaitAsync(run => run.BranchId == forkId && run.Status == ChatRunStatus.Completed);
        main.Answer.SetResult("Main reply");
        await fixture.WaitAsync(run => run.BranchId == fixture.ChatId && run.Status == ChatRunStatus.Completed);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Single(message => message.Content == "Main reply").ParentId.ShouldBe(root);
        chat.Messages.Single(message => message.Content == "Fork reply").ParentId.ShouldBe(forkId);
    }

    [Fact]
    public async Task StopAndResumeShouldReuseTheCommittedUserMessage()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Question"));
        await fixture.NextCallAsync();
        await fixture.Dispatcher.StopAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None);
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Paused);
        // Wait for the cancelled worker to finish before resuming.
        await fixture.Dispatcher.ShutdownAsync(CancellationToken.None);
        await fixture.RestartAsync();
        await fixture.Dispatcher.ResumeAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None);
        (await fixture.NextCallAsync()).Answer.SetResult("Reply");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Count(message => message.Role == "User").ShouldBe(1);
    }

    [Fact]
    public async Task FailedQueueCommitMustNotDeleteTheReplacedMessages()
    {
        await using var fixture = await Fixture.CreateAsync();
        var source = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), source, "Keep me"));
        (await fixture.NextCallAsync()).Answer.SetResult("Keep reply");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        fixture.FileSystem.FailWriteSuffix = ".run.json.tmp";
        await Should.ThrowAsync<IOException>(() => fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Replacement",
            ChatSubmitMode.Replace, ReplaceSourceId: source)));
        fixture.FileSystem.FailWriteSuffix = null;
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Select(message => message.Content).ShouldBe(["Keep me", "Keep reply"]);
    }

    [Fact]
    public async Task ImmediateResumeAfterStopShouldFinishWithoutDuplicatingTheUserMessage()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Question"));
        await fixture.NextCallAsync();
        await fixture.Dispatcher.StopAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None);
        await fixture.Dispatcher.ResumeAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None);
        (await fixture.NextCallAsync()).Answer.SetResult("Reply");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Select(message => message.Content).ShouldBe(["Question", "Reply"]);
    }

    [Fact]
    public async Task DeletingAnActiveChatShouldCancelItsWorkerAndRemovePersistedRuns()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Question"));
        var call = await fixture.NextCallAsync();
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        var result = await fixture.Dispatcher.DeleteChatAsync(fixture.ProjectId, fixture.ChatId, chat!.Revision, CancellationToken.None);
        result.IsDeleted.ShouldBeTrue();
        call.Answer.TrySetResult("Too late");
        (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None)).ShouldBeNull();
        await fixture.RestartAsync();
        (await fixture.Dispatcher.GetSnapshotAsync(CancellationToken.None)).ShouldBeEmpty();
    }

    private sealed record Call(ChatCompletionRequest Request, TaskCompletionSource<string> Answer)
    {
        public IReadOnlyList<ChatToolCall>? ToolCalls { get; set; }
    }
    private sealed class Completion : IChatCompletionClient
    {
        public Channel<Call> Calls { get; } = Channel.CreateUnbounded<Call>();
        public Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(ChatCompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var call = new Call(request, new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));
            Calls.Writer.TryWrite(call);
            var content = await call.Answer.Task.WaitAsync(cancellationToken);
            yield return new ChatCompletionChunk(content, ToolCalls: call.ToolCalls);
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public MemoryFileSystem FileSystem { get; } = new();
        public Completion Completion { get; } = new();
        public TestTools Tools { get; } = new();
        private readonly ChatSynchronization _synchronization = new();
        private readonly SystemClock _clock = new();
        private readonly Uuid7IdGenerator _ids = new();
        private readonly IGlobalSecretStore _secrets = Mock.Of<IGlobalSecretStore>();
        private readonly JsonChatRepository _chatRepository;
        private readonly JsonProjectRepository _projects;
        private readonly JsonChatRunRepository _runs;
        private readonly JsonGlobalSettingsRepository _settings;
        private readonly ProjectService _projectService;
        public ChatService Chats { get; }
        public ChatRunDispatcher Dispatcher { get; private set; }
        public Guid ProjectId { get; private set; }
        public Guid ChatId { get; private set; }
        private Fixture()
        {
            _chatRepository = new JsonChatRepository(FileSystem, new ChatStoragePaths("data"));
            _projects = new JsonProjectRepository(FileSystem, new ProjectStoragePaths("data"));
            _runs = new JsonChatRunRepository(FileSystem, new ChatRunStoragePaths("data"));
            _settings = new JsonGlobalSettingsRepository(FileSystem, new GlobalSettingsPaths("data"));
            _projectService = new ProjectService(_projects, _ids, _clock, _settings);
            Chats = new ChatService(_chatRepository, _ids, _clock, _synchronization);
            Dispatcher = NewDispatcher();
        }
        private ChatRunDispatcher NewDispatcher() => new(_runs, Chats, _projectService, _settings,
            new ChatAgent(Completion, Tools, _projectService, _settings), _secrets, _clock, _synchronization);
        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            await fixture._settings.SaveAsync(new GlobalSettings([new ConnectionSettings(Guid.NewGuid(), "Test", "https://example.test/v1", "model", true, true, false)], []), CancellationToken.None);
            fixture.ProjectId = (await fixture._projectService.CreateAsync(new CreateProjectRequest("Test", ""), CancellationToken.None)).Id;
            fixture.ChatId = (await fixture.Chats.CreateAsync(fixture.ProjectId, new CreateChatRequest("Chat"), CancellationToken.None)).Id;
            await fixture.Dispatcher.WarmUpAsync(CancellationToken.None);
            return fixture;
        }
        public Task<ChatRunSnapshot> SubmitAsync(SubmitChatMessageRequest request) => Dispatcher.SubmitAsync(ProjectId, ChatId, request, CancellationToken.None);
        public async Task SetPolicyAsync(string decision)
        {
            var global = await _settings.LoadAsync(CancellationToken.None);
            await _settings.SaveAsync(global with { McpServers = [DefaultMcpServer.Settings with { Policy = "Allow" }] }, CancellationToken.None);
            var project = await _projectService.GetAsync(ProjectId, CancellationToken.None);
            await _projectService.UpdateSecurityAsync(ProjectId, new UpdateProjectSecurityRequest(project!.Revision, [],
                [new Contracts.Projects.McpServerSettings(DefaultMcpServer.Id, "Default", "Stdio", true)],
                [new ToolPolicySettings(DefaultMcpServer.Id, "process_run", "schema", decision, 20, 120)]), CancellationToken.None);
        }
        public async Task<Call> NextCallAsync() => await Completion.Calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        public async Task<ChatRunSnapshot> WaitAsync(Func<ChatRunSnapshot, bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await foreach (var snapshots in Dispatcher.SubscribeAsync(timeout.Token))
                if (snapshots.FirstOrDefault(predicate) is { } snapshot) return snapshot;
            throw new InvalidOperationException("No matching snapshot.");
        }
        public async Task RestartAsync()
        {
            await Dispatcher.DisposeAsync();
            Dispatcher = NewDispatcher();
            await Dispatcher.WarmUpAsync(CancellationToken.None);
        }
        public async ValueTask DisposeAsync()
        {
            await Dispatcher.DisposeAsync();
            _chatRepository.Dispose();
            _projects.Dispose();
            _runs.Dispose();
        }
    }

    private sealed class TestTools : IToolSessionFactory, IToolSession
    {
        public int CallCount { get; private set; }
        public int OpenCount { get; private set; }
        public IReadOnlyList<AgentTool> Tools { get; } = [new(new ChatToolDefinition("mcp_default__process_run", "Run", JsonSerializer.Deserialize<JsonElement>("{}")), DefaultMcpServer.Id, "process_run", "schema")];
        public Task<IToolSession> OpenAsync(CancellationToken cancellationToken)
        {
            OpenCount++;
            return Task.FromResult<IToolSession>(this);
        }
        public string ValidateArguments(AgentTool tool, string arguments) => arguments;
        public Task<string> CallAsync(AgentTool tool, string arguments, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult("{\"exitCode\":0}");
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
