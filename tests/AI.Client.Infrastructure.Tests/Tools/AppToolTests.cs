namespace AI.Client.Infrastructure.Tests.Tools;

using AI.Client.Application.Chat;
using AI.Client.Application.Chats;
using AI.Client.Application.Notifications;
using AI.Client.Application.Projects;
using AI.Client.Application.Runs;
using AI.Client.Application.Settings;
using AI.Client.Application.Tools;
using AI.Client.Application.Workspace;
using AI.Client.Contracts.Chats;
using AI.Client.Contracts.Projects;
using AI.Client.Contracts.Runs;
using AI.Client.Contracts.Settings;
using AI.Client.Contracts.Tools;
using AI.Client.Infrastructure.Projects;
using AI.Client.Infrastructure.Chat;
using AI.Client.Infrastructure.Settings;
using AI.Client.Infrastructure.Storage;
using AI.Client.Infrastructure.Tests.Storage;
using AI.Client.Infrastructure.Tools;
using AI.Client.Infrastructure.Workspace;
using AI.Client.Mcp.App;
using Moq;
using Shouldly;
using System.Text.Json;
using Xunit;

/// <summary>
/// Exercises the application server the way the Host does: over a real MCP session, through the
/// protocol's own <c>tools/list</c> and <c>tools/call</c>, against real application services.
/// Nothing here reaches into a tool class directly, so a schema or transport regression fails here
/// rather than in production.
/// </summary>
public sealed class AppToolTests
{
    [Fact]
    public async Task ShouldDiscoverEveryToolOverTheInProcessTransport()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        // The server decides its own listing order, so the set is what matters, not the sequence.
        session.Tools.Select(tool => tool.OriginalName).Order(StringComparer.Ordinal).ShouldBe(
            ["app_chats", "app_projects", "app_read", "app_runs", "app_security", "ask_user", "spawn_subtask"]);
        session.Tools.ShouldAllBe(tool => tool.ServerId == AppMcpServer.Id);
        session.Tools.ShouldAllBe(tool => tool.ModelDefinition.Name.StartsWith("mcp_app__", StringComparison.Ordinal));
        // A schema hash is what ties a saved policy to the tool it was granted for.
        session.Tools.ShouldAllBe(tool => tool.SchemaHash.Length == 64);
    }

    [Fact]
    public async Task ProjectToolShouldDescribeDirectoryBasedNameSuggestion()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var description = session.Tools.Single(tool => tool.OriginalName == "app_projects").ModelDefinition.Description;

        description.ShouldContain("derive a suggested project name");
        description.ShouldContain("final segment");
        description.ShouldContain("(Recommended)");
        description.ShouldContain("allow a custom name");
    }

    [Fact]
    public async Task ShouldReadProjectsAndChats()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var projects = await AppFixture.CallAsync(session, "app_read", new { resource = "Projects" });
        projects.GetProperty("total").GetInt32().ShouldBe(1);
        projects.GetProperty("items")[0].GetProperty("name").GetString().ShouldBe("Test");

        var chats = await AppFixture.CallAsync(session, "app_read", new { resource = "Chats", projectId = fixture.ProjectId });
        chats.GetProperty("items")[0].GetProperty("title").GetString().ShouldBe("Chat");
    }

    [Fact]
    public async Task ShouldNotReturnSecretsWhenReadingSettings()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var settings = await AppFixture.CallAsync(session, "app_read", new { resource = "Settings" });

        var connection = settings.GetProperty("items")[0].GetProperty("connections")[0];
        connection.TryGetProperty("hasCredential", out _).ShouldBeTrue();
        // Whatever shape the settings document grows, a key must never be part of it.
        settings.GetRawText().ShouldNotContain("apiKey", Case.Insensitive);
        settings.GetRawText().ShouldNotContain("secret", Case.Insensitive);
    }

    [Fact]
    public async Task ShouldPageAndContinueFromTheCursorItReturned()
    {
        await using var fixture = await AppFixture.CreateAsync();
        for (var index = 0; index < 5; index++)
            await fixture.Chats.CreateAsync(fixture.ProjectId, new CreateChatRequest($"Chat {index}"), CancellationToken.None);
        await using var session = await fixture.OpenAsync();

        var first = await AppFixture.CallAsync(session, "app_read",
            new { resource = "Chats", projectId = fixture.ProjectId, limit = 2 });
        first.GetProperty("returned").GetInt32().ShouldBe(2);
        first.GetProperty("total").GetInt32().ShouldBe(6);
        var cursor = first.GetProperty("nextCursor").GetString();
        cursor.ShouldNotBeNull();

        var second = await AppFixture.CallAsync(session, "app_read",
            new { resource = "Chats", projectId = fixture.ProjectId, limit = 2, cursor });
        second.GetProperty("returned").GetInt32().ShouldBe(2);
        // The second page must not repeat the first one's items.
        var firstIds = first.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray();
        second.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()).ShouldAllBe(id => !firstIds.Contains(id));
    }

    [Fact]
    public async Task ShouldCreateAChatAndAnnounceTheChange()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        using var changed = fixture.WatchChanges();

        var result = await AppFixture.CallAsync(session, "app_chats",
            new { operation = "Create", projectId = fixture.ProjectId, operationId = Guid.NewGuid(), title = "From a tool" });

        result.GetProperty("applied").GetBoolean().ShouldBeTrue();
        var chats = await fixture.Chats.ListAsync(fixture.ProjectId, CancellationToken.None);
        chats.ShouldContain(chat => chat.Title == "From a tool");
        (await changed.WaitAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task ShouldReplayARepeatedOperationInsteadOfApplyingItTwice()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        var operationId = Guid.NewGuid();
        var arguments = new { operation = "Create", projectId = fixture.ProjectId, operationId, title = "Only once" };

        var first = await AppFixture.CallAsync(session, "app_chats", arguments);
        var second = await AppFixture.CallAsync(session, "app_chats", arguments);

        second.GetProperty("replayed").GetBoolean().ShouldBeTrue();
        second.GetProperty("chatId").GetString().ShouldBe(first.GetProperty("chatId").GetString());
        (await fixture.Chats.ListAsync(fixture.ProjectId, CancellationToken.None))
            .Count(chat => chat.Title == "Only once").ShouldBe(1);
    }

    [Fact]
    public async Task ShouldOnlyDescribeADeletionUntilDryRunIsTurnedOff()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);

        var planned = await AppFixture.CallAsync(session, "app_chats", new
        {
            operation = "Delete", projectId = fixture.ProjectId, chatId = fixture.ChatId,
            operationId = Guid.NewGuid(), revision = chat!.Revision,
        });

        planned.GetProperty("dryRun").GetBoolean().ShouldBeTrue();
        planned.GetProperty("applied").GetBoolean().ShouldBeFalse();
        planned.GetProperty("effect").GetString().ShouldNotBeNull().ShouldContain("Would delete");
        (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None)).ShouldNotBeNull();

        var applied = await AppFixture.CallAsync(session, "app_chats", new
        {
            operation = "Delete", projectId = fixture.ProjectId, chatId = fixture.ChatId,
            operationId = Guid.NewGuid(), revision = chat.Revision, dryRun = false,
        });

        applied.GetProperty("applied").GetBoolean().ShouldBeTrue();
        (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task ShouldReportTheCurrentStateWhenTheRevisionIsStale()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var result = await AppFixture.CallAsync(session, "app_chats", new
        {
            operation = "Rename", projectId = fixture.ProjectId, chatId = fixture.ChatId,
            operationId = Guid.NewGuid(), title = "Renamed", revision = 999L,
        }, expectError: true);

        result.GetProperty("status").GetString().ShouldBe("Conflict");
        result.GetProperty("applied").GetBoolean().ShouldBeFalse();
        // The caller is handed what it needs to decide for itself, rather than a bare failure.
        result.GetProperty("revision").GetInt64().ShouldBeGreaterThan(0);
        result.GetProperty("current").GetProperty("title").GetString().ShouldBe("Chat");
    }

    [Fact]
    public async Task ShouldChangeProjectSecurity()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        var project = await fixture.Projects.GetAsync(fixture.ProjectId, CancellationToken.None);

        var result = await AppFixture.CallAsync(session, "app_security", new
        {
            operation = "SetProjectSecurity",
            projectId = fixture.ProjectId,
            operationId = Guid.NewGuid(),
            revision = project!.Revision,
            security = new
            {
                directoryGrants = new[]
                {
                    new { id = Guid.NewGuid(), displayName = "src", canonicalRoot = @"C:\Projects\Demo", recursive = true, toolNames = ReadOnlyCapability },
                },
                mcpServers = Array.Empty<object>(),
                toolPolicies = Array.Empty<object>(),
            },
        });

        result.GetProperty("applied").GetBoolean().ShouldBeTrue();
        var updated = await fixture.Projects.GetAsync(fixture.ProjectId, CancellationToken.None);
        updated!.DirectoryGrants.ShouldHaveSingleItem().CanonicalRoot.ShouldBe(@"C:\Projects\Demo");
    }

    [Fact]
    public async Task ShouldFindMessagesWithoutReadingChatsOneByOne()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await fixture.Chats.AppendMessageAsync(fixture.ProjectId, fixture.ChatId,
            new AppendChatMessageRequest(null, null, "User", "remember the deploy checklist", 1), CancellationToken.None);
        await using var session = await fixture.OpenAsync();

        var found = await AppFixture.CallAsync(session, "app_read", new { resource = "Search", query = "deploy" });

        found.GetProperty("resource").GetString().ShouldBe("Search");
        var match = found.GetProperty("items")[0];
        match.GetProperty("chatId").GetString().ShouldBe(fixture.ChatId.ToString());
        match.GetProperty("snippet").GetString().ShouldNotBeNull().ShouldContain("deploy");
    }

    [Fact]
    public async Task ShouldRefuseASearchWithNoQuery()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var result = await AppFixture.CallAsync(session, "app_read", new { resource = "Search" }, expectError: true);

        result.GetProperty("error").GetString().ShouldNotBeNull().ShouldContain("query");
    }

    [Fact]
    public async Task ShouldRefuseToRehearseAnOperationThatCannotBeRehearsed()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var result = await AppFixture.CallAsync(session, "app_chats", new
        {
            operation = "Create", projectId = fixture.ProjectId, operationId = Guid.NewGuid(),
            title = "Rehearsed", dryRun = true,
        }, expectError: true);

        // Asking for a rehearsal that cannot happen must fail loudly; applying the change instead
        // would be exactly the surprise the flag exists to prevent.
        result.GetProperty("error").GetString().ShouldNotBeNull().ShouldContain("cannot be rehearsed");
        (await fixture.Chats.ListAsync(fixture.ProjectId, CancellationToken.None))
            .ShouldNotContain(chat => chat.Title == "Rehearsed");
    }

    [Fact]
    public async Task ShouldRejectArgumentsThatDoNotMatchTheDeclaredSchema()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        var tool = session.Tools.Single(item => item.OriginalName == "app_read");

        // 'resource' is a closed enum in the schema, so the Host rejects this before the server sees it.
        var error = Should.Throw<ArgumentException>(() => session.ValidateArguments(tool, """{"resource":"Everything"}"""));
        // The message has to say what was wrong with which property, or the caller can only guess.
        error.Message.ShouldContain("resource");
    }

    private static readonly IReadOnlySet<Guid> AppServerOnly = new HashSet<Guid> { AppMcpServer.Id };

    private static readonly string[] ReadOnlyCapability = ["read"];

    [Fact]
    public async Task AskUserShouldReturnTheChosenLabelsAndNameWhatWasLeftOpen()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        fixture.Broker.Answer = request => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered,
            [new UserPromptAnswer("scope", [1], null)]);

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[]
            {
                new { id = "scope", text = "How far?", options = new[] { new { label = "Narrow" }, new { label = "Wide" } } },
                new { id = "tests", text = "Add tests?", options = new[] { new { label = "Yes" }, new { label = "No" } } }
            }
        });

        result.GetProperty("outcome").GetString().ShouldBe("answered");
        var answers = result.GetProperty("answers").EnumerateArray().ToArray();
        // Positions travel over the wire; labels are what the model and the transcript get.
        answers.ShouldHaveSingleItem().GetProperty("selected")[0].GetString().ShouldBe("Wide");
        result.GetProperty("guidance").GetString()!.ShouldContain("tests");
    }

    [Fact]
    public async Task AskUserShouldDropAnAnswerNamingAnOptionThatWasNeverOffered()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        fixture.Broker.Answer = _ => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered,
            [new UserPromptAnswer("scope", [7], null), new UserPromptAnswer("gone", [0], null)]);

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[] { new { id = "scope", text = "How far?", options = new[] { new { label = "Narrow" } } } }
        });

        result.GetProperty("answers").GetArrayLength().ShouldBe(0);
        result.GetProperty("guidance").GetString()!.ShouldContain("state the assumption");
    }

    [Fact]
    public async Task AskUserShouldCarryFreeTextThroughUntouched()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        fixture.Broker.Answer = _ => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered,
            [new UserPromptAnswer("name", [], "  Контрагенты  ")]);

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[] { new { id = "name", text = "What should it be called?", options = Array.Empty<object>() } }
        });

        result.GetProperty("answers")[0].GetProperty("other").GetString().ShouldBe("Контрагенты");
    }

    [Fact]
    public async Task AskUserShouldReturnSeveralSelectedDirectories()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        fixture.Broker.Answer = _ => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered,
            [new UserPromptAnswer("directories", [], null,
                [@" C:\Projects\One ", @"C:\Projects\Two", @"c:\projects\one"])]);

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[]
            {
                new { id = "directories", text = "Select project directories", options = Array.Empty<object>(), pathKind = "directories" }
            }
        });

        var answer = result.GetProperty("answers")[0];
        answer.GetProperty("paths").EnumerateArray().Select(item => item.GetString())
            .ShouldBe([@"C:\Projects\One", @"C:\Projects\Two"]);
        fixture.Broker.LastRequest!.Questions[0].PathKind.ShouldBe("directories");
    }

    [Theory]
    // Every limit here is about the card staying readable, and each one is reported in words the
    // model can act on rather than as a schema failure it can only repeat.
    [InlineData("no questions")]
    [InlineData("duplicate ids")]
    [InlineData("unanswerable")]
    public async Task AskUserShouldRefuseAQuestionNobodyCouldRead(string kind)
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        object arguments = kind switch
        {
            "no questions" => new { questions = Array.Empty<object>() },
            "duplicate ids" => new
            {
                questions = new[]
                {
                    new { id = "a", text = "One?", options = new[] { new { label = "Yes" } }, allowOther = true },
                    new { id = "a", text = "Two?", options = new[] { new { label = "Yes" } }, allowOther = true }
                }
            },
            _ => new { questions = new[] { new { id = "a", text = "One?", options = Array.Empty<object>(), allowOther = false } } }
        };

        var result = await AppFixture.CallAsync(session, "ask_user", arguments, expectError: true);
        result.GetProperty("outcome").GetString().ShouldBe("invalid");
        result.GetProperty("error").GetString().ShouldNotBeNullOrWhiteSpace();
        fixture.Broker.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task AskUserInABackgroundRunShouldAnswerItselfAndSendTheQuestionUpwards()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync(interactive: false);

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[] { new { id = "scope", text = "How far?", options = new[] { new { label = "Narrow" } } } }
        });

        result.GetProperty("outcome").GetString().ShouldBe("dismissed");
        result.GetProperty("guidance").GetString()!.ShouldContain("final answer");
        // Nobody was asked, rather than asked and timed out.
        fixture.Broker.LastRequest.ShouldBeNull();
    }

    private sealed class AppFixture : IAsyncDisposable
    {
        private readonly MemoryFileSystem _fileSystem = new();
        private readonly ChatSynchronization _synchronization = new();
        private readonly SystemClock _clock = new();
        private readonly Uuid7IdGenerator _ids = new();
        private readonly IGlobalSecretStore _secrets = Mock.Of<IGlobalSecretStore>();
        private readonly JsonGlobalSettingsRepository _settings;
        private readonly AppDataChangeSignal _signal = new();
        private readonly CompositeToolSessionFactory _sessions;

        /// <summary>Stands in for the run waiting on the question, so the tool can be exercised alone.</summary>
        public TestPromptBroker Broker { get; } = new();

        public ProjectService Projects { get; }
        public ChatService Chats { get; }
        public Guid ProjectId { get; private set; }
        public Guid ChatId { get; private set; }

        private AppFixture()
        {
            _settings = new JsonGlobalSettingsRepository(_fileSystem, new GlobalSettingsPaths("data"));
            var projectRepository = new JsonProjectRepository(_fileSystem, new ProjectStoragePaths("data"), new ProjectDocumentSerializer());
            var chatRepository = new JsonChatRepository(_fileSystem, new ChatStoragePaths("data"), new ChatDocumentSerializer());
            var runRepository = new JsonChatRunRepository(_fileSystem, new ChatRunStoragePaths("data"));
            Projects = new ProjectService(projectRepository, _ids, _clock, _settings);
            Chats = new ChatService(chatRepository, _ids, _clock, _synchronization);
            var settingsService = new GlobalSettingsService(_settings, _secrets, new ConnectionContextLimitsResolver());
            IWorkspaceChangeTracker workspace = new WorkspaceChangeTracker();
            var policies = new ToolPolicyResolver(Projects, Chats, _settings);
            var modelProjector = new ToolResultModelProjector();
            var toolResultCodec = new ToolResultCodec(modelProjector);
            var instructionRegistry = new ModelInstructionRegistry();
            var dispatcher = new ChatRunDispatcher(runRepository, Chats, Chats, Projects, _settings, settingsService,
                new ChatAgent(Mock.Of<AI.Client.Application.Chat.IChatCompletionClient>(), Mock.Of<IToolSessionFactory>,
                    Projects, _settings, policies, workspace, modelProjector, toolResultCodec,
                    new ChatContextPlanner(new ContextTokenEstimator(), new ChatContextCompactor(new ContextTokenEstimator()),
                        new ConnectionContextLimitsResolver()),
                    Mock.Of<IContextPlanDiagnostics>(), new ChatTransportActivity(),
                    new ToolDefinitionSelector(new ContextTokenEstimator(), new ConnectionContextLimitsResolver(), new ToolSelectionPriorityPolicy()),
                    new ToolCatalogRegistry(), new ModelContentCheckpointService(), instructionRegistry,
                    new ModelInstructionComposer(instructionRegistry, new ContextTokenEstimator()),
                    Mock.Of<IModelInstructionDiagnostics>(), new RunCompletionProtocol(),
                    new ToolSearchDefinitionEnricher(new ContextTokenEstimator())),
                _secrets, _clock, _ids, _synchronization, workspace, policies, new ChatContext(toolResultCodec), new ChatBranchIds());
            var writes = new AppWrites(new AppOperationLog(), _signal, new AppToolReply());
            var presentations = new ToolPresentations(
                new GenericToolPresentationAdapter(),
                [
                    new FileToolPresentationAdapter(), new ProcessToolPresentationAdapter(), new WebToolPresentationAdapter(),
                    new AppReadPresentationAdapter(), new AppWritePresentationAdapter(), new AppSubtaskPresentationAdapter(),
                ]);
            IEnumerable<IAppTool> tools =
            [
                new AppReadTool(Projects, Chats, settingsService, new ChatSearchService(Projects, Chats), () => dispatcher, new AppToolReply()),
                new AppChatsTool(Chats, () => dispatcher, writes, new AppToolReply()),
                new AppRunsTool(() => dispatcher, writes, new AppToolReply()),
                new AppProjectsTool(Projects, Chats, () => dispatcher, writes, new AppToolReply()),
                new AppSecurityTool(Projects, Chats, settingsService, writes, new AppToolReply()),
                new AppSubtaskTool(() => throw new InvalidOperationException("not used"), Projects, Chats, _settings, _secrets,
                    presentations, toolResultCodec, new AppToolReply()),
                new AppAskUserTool(() => Broker),
            ];
            IMcpServerConnection connection = new AppToolSessionFactory(new AppMcpServerHost(tools, new AppToolReply()), modelProjector);
            _sessions = new CompositeToolSessionFactory([connection]);
        }

        public static async Task<AppFixture> CreateAsync()
        {
            var fixture = new AppFixture();
            await fixture._settings.SaveAsync(new GlobalSettings(
                [new ConnectionSettings(Guid.NewGuid(), "Test", "https://example.test/v1", "model", true, true, false)],
                [], []), CancellationToken.None);
            fixture.ProjectId = (await fixture.Projects.CreateAsync(new CreateProjectRequest("Test", ""), CancellationToken.None)).Id;
            fixture.ChatId = (await fixture.Chats.CreateAsync(fixture.ProjectId, new CreateChatRequest("Chat"), CancellationToken.None)).Id;
            return fixture;
        }

        public Task<IToolSession> OpenAsync(bool interactive = true) =>
            _sessions.OpenAsync([], AppServerOnly, new ToolRunContext(ProjectId, ChatId, ChatId, interactive),
                TestContext.Current.CancellationToken);

        /// <summary>Calls a tool the way the agent does, and hands back its structured result.</summary>
        public static async Task<JsonElement> CallAsync(IToolSession session, string name, object arguments, bool expectError = false)
        {
            var tool = session.Tools.Single(item => item.OriginalName == name);
            var result = await session.CallAsync(tool, JsonSerializer.Serialize(arguments), null,
                TestContext.Current.CancellationToken);
            result.IsError.ShouldBe(expectError);
            return result.StructuredContent!.Value;
        }

        public ChangeWatch WatchChanges() => new(_signal);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Answers a question however the test wants it answered, and keeps what was asked. The real
    /// broker is the dispatcher, which needs a live run; this one needs nothing, which is what lets
    /// the tool's own behaviour be tested without one.
    /// </summary>
    private sealed class TestPromptBroker : IUserPromptBroker
    {
        public UserPromptRequest? LastRequest { get; private set; }

        public Func<UserPromptRequest, UserPromptResponse> Answer { get; set; } =
            request => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered,
                request.Questions.Select(question => new UserPromptAnswer(question.Id, [0], null)).ToArray());

        public Task<UserPromptResponse> AskAsync(ToolRunContext run, UserPromptRequest request, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Answer(request));
        }
    }

    /// <summary>Waits for the Host to announce that application data moved.</summary>
    private sealed class ChangeWatch : IDisposable
    {
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(10));
        private readonly Task<bool> _signalled;

        public ChangeWatch(IAppDataChangeSignal signal)
        {
            _signalled = Task.Run(async () =>
            {
                await foreach (var _ in signal.SubscribeAsync(_stop.Token)) return true;
                return false;
            });
        }

        public async Task<bool> WaitAsync()
        {
            try { return await _signalled; }
            catch (OperationCanceledException) { return false; }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _stop.Dispose();
        }
    }
}
