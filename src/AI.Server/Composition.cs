// DI guide: [Pure.DI conventions](../../docs/30-dependency-injection.md).
// ReSharper disable UnusedMember.Local
namespace AI.Server;

using System.Diagnostics;
using Application.Chat;
using Application.Chats;
using Application.Instructions;
using Application.Memory;
using Application.Notifications;
using Application.Projects;
using Application.Runs;
using Application.Resources;
using Application.Settings;
using Application.Skills;
using Application.Tools;
using Application.Workspace;
using Application.Usage;
using AI.Contracts.Workspace;
using AI.Contracts.Navigation;
using Hosting;
using Hosting.Endpoints;
using Infrastructure.Chat;
using Infrastructure.Credentials;
using Infrastructure.Logging;
using Infrastructure.Projects;
using Infrastructure.Settings;
using Infrastructure.Storage;
using Infrastructure.Tools;
using Infrastructure.Workspace;
using Infrastructure.Usage;
using Microsoft.Extensions.Logging;
using Mcp.App;
using Pure.DI;
using AI.Updates;

/// <summary>
/// Shared server bindings without consumer roots. Executables and tests link this internal setup.
/// ASP.NET hosts use <c>DependsOn("AI.Server.AspNetComposition")</c> to include service-provider
/// roots; direct composition consumers depend on this setup and declare their own named roots.
/// </summary>
internal sealed class Composition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup(kind: CompositionKind.Internal)
            .Hint(Hint.Comments, "Off")
            // Pure.DI 2.5.4 miscompiles lightweight anonymous roots for this graph: a singleton
            // shared by several deferred factories (AppDataChangeSignal, via AppWrites) is used
            // before it is created, so AppWrites gets null. Off until that is fixed upstream.
            .Hint(Hint.LightweightAnonymousRoot, "Off")
            // What the entry point decided before starting the server: its command line, parsed.
            .Arg<ServerOptions>("options")
            .Transient<ChatArchiveService>()
            // Preview tickets must survive across the describe and content HTTP requests.
            .Singleton<FilePreviewService>()
            .Singleton<HostUpdateService>()
            .Transient<UpdateManagerFactory, GitHubUpdateFeed, UpdateInstaller, UpdateInstallationProvider>()
            .Singleton<BrowserAccessService>()
            .Transient<AiClientServer, ApiExceptionHandler, WebClientHost, HostDescriptor, ChatEndpoint,
                RunEventsPublisher, RunSnapshotComparer, InstalledDesktop>()
            .Transient<HealthEndpoints, RunEndpoints, ChatEndpoints, ProjectEndpoints, SettingsEndpoints,
                ChatCompletionEndpoints, FileSystemEndpoints, FilePreviewEndpoints, GitEndpoints, MemoryEndpoints,
                SkillEndpoints, BrowserAccessEndpoints, UsageEndpoints, HistoryCheckpointEndpoints, UpdateEndpoints,
                AppGuideEndpoints>(Tag.Unique)
            .Singleton((IProjectStorageLocation location) => new JsonLineFileLoggerProvider(location))
            .Transient<ProjectStorageLocation, DataDirectoryLock, ProjectStoragePaths, ChatStoragePaths,
                ChatRunStoragePaths, GlobalSettingsPaths>()
            // Credentials: DPAPI on Windows; elsewhere AES-GCM under a key in the system keyring,
            // or in the data directory when the machine has no working keyring.
            .Singleton<IUserDataProtector>(ctx =>
            {
                if (OperatingSystem.IsWindows())
                {
                    ctx.Inject<ProtectedDataUserDataProtector>(out var windows);
                    return windows;
                }

                ctx.Inject<MasterKeyUserDataProtector>(out var portable);
                return portable;
            })
            .Transient<IKeyringMasterKeyStore>(ctx =>
            {
                if (OperatingSystem.IsMacOS())
                {
                    ctx.Inject<KeychainMasterKeyStore>(out var keychain);
                    return keychain;
                }

                ctx.Inject<SecretServiceMasterKeyStore>(out var secretService);
                return secretService;
            })
            .Bind<IFileMasterKeyStore>().To<FileMasterKeyStore>()
            .Singleton<JsonProjectRepository, JsonChatRepository, ChatSynchronization, ChatTransportActivity,
                JsonGlobalSettingsRepository, JsonResourceRepository, JsonReviewRepository, JsonMemoryRepository,
                JsonProjectInstructionsRepository, JsonChatRunRepository, ChatRunDispatcher,
                ModelContentCheckpointService, ModelInstructionRegistry, ToolCatalogRegistry, WorkspaceChangeTracker,
                AppDataChangeSignal, AppNavigationSignal, AppOperationLog>()
            .Transient<PhysicalTextFileSystem, PhysicalDirectoryBrowser, ProjectDocumentSerializer, Uuid7IdGenerator,
                SystemClock, ProjectService, ChatDocumentSerializer, ChatService, ChatSearchService, PinOrderKeys,
                ChatCompletionSseParser, ContextPlanDiagnostics, ChatTransportPolicy, ProtectedGlobalSecretStore,
                ResourceService, ResourceModelProjection, ProjectPathAccess, WorkspacePathResolver, ReviewService,
                MemoryService, ProjectInstructionsService, WorkspaceInstructionFileReader, StandingInstructions,
                GlobalSettingsService, OpenAiCompatibleConnectionModelsResolver, ChatContext, ChatAgent,
                ContextTokenEstimator, ChatContextCompactor, ChatContextPlanner, KeyringOrFileMasterKeyStore,
                ModelInstructionComposer, ToolDefinitionSelector, ToolSelectionPriorityPolicy,
                ToolSearchDefinitionEnricher, ToolPolicyResolver, LineDiff, MasterKeyFormat, ProcessCommandRunner,
                AppWrites, AppMcpServerHost, ExternalToolSessionFactory, ChatBranchIds, ToolUserInterface>()
            .Singleton<WorkspaceFileSearch>()
            .Transient<GitWorkspaceDiffReader, GitBrowser, FileExcerptReader>()
            .Singleton<TokenUsageMeter, JsonLinesTokenUsageLedger>()
            .Transient<ChatCompletionUsageReader, TokenUsageAggregator, TokenUsageService>()
            .Singleton<PromptPrefixTracker, UsageCostEstimator, ConnectionRateLimits>()
            .Transient<RateLimitHeaderReader>()
            .Singleton<JsonHistoryCheckpointRepository>()
            .Transient<ContextSummaryWriter, HistoryCheckpointService, ChatHistoryCompaction>()
            // Executors share a contract; tags identify the role without concrete dependencies.
            .Transient<ChatRenameSkill>("chat-rename")
            .Transient<ChatReplySuggestSkill>("chat-reply-suggest")
            .Transient<ChatCommentSuggestSkill>("chat-comment-suggest")
            .Transient<SkillRouteSkill>("skill-route")
            .Transient<ChatToolRiskAssessSkill>("chat-tool-risk-assess")
            .Bind<IChatReplySuggestions>().As(Lifetime.Singleton).To<ChatReplySuggestions>()
            .Singleton<SkillCatalog>()
            .Transient<ToolAutoApprover, GuideChats, ConnectionChoice, ReviewCommentSuggestions, AppToolReply>()
            // Reading embedded skill definitions once avoids repeated parsing.
            .Singleton<BuiltInSkillCatalog>("built-in")
            .Singleton<SkillRunner>()
            // Pure.DI 2.5.4 emits out-of-scope factory locals for a transient composite here.
            // Keep this one cache until the generator issue recorded in docs/pure-di-issues.md is fixed.
            .Singleton<CompositeToolSessionFactory>()
            .Transient<GenericSkillExecutor, SkillGuide, SkillRouting>()
            .Singleton<OpenAiCompatibleChatCompletionClient>("base")
            .Transient((
                [Tag("base")] IChatCompletionClient baseClient,
                ILogger<RetryingChatCompletionClient> retryLogger,
                IChatTransportPolicy transportPolicy,
                IChatTransportActivity transportActivity,
                ITokenUsageMeter usageMeter,
                IContextTokenEstimator usageEstimator,
                IPromptPrefixTracker prefixes) =>
                new MeteringChatCompletionClient(
                    new RetryingChatCompletionClient(baseClient, retryLogger, transportPolicy, transportActivity),
                    usageMeter, usageEstimator, prefixes))
            .Transient<AppReadTool, AppChatsTool, AppRunsTool, AppProjectsTool, AppSecurityTool, AppSubtaskTool,
                AppAskUserTool, AppToolSearchTool, AppContextCompactTool, AppResourcesTool, AppMemoryTool,
                AppInstructionsTool, AppSkillSearchTool, AppSkillRunTool, AppSkillsTool, AppNavigateTool>(Tag.Unique)
            .Transient<AppNavigationTargets, AppGuideTopics, AppGuideLanguageContext>()
            .Transient<AppToolSessionFactory>(Tag.Type)
            .Transient<DefaultToolSessionFactory, CSharpToolSessionFactory>(Tag.Unique)
            .Singleton(_ => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
}

/// <summary>Only the roots resolved by ASP.NET handlers and hosted services.</summary>
internal sealed class AspNetComposition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup(kind: CompositionKind.Internal)
            .Hint(Hint.Comments, "Off")
            .DependsOn("AI.Server.Composition")
            .Root<IHostDescriptor>()
            .Root<IDirectoryBrowser>()
            .Root<IGitBrowser>()
            .Root<IProjectService>()
            .Root<IChatService>()
            .Root<IChatArchiveService>()
            .Root<ISkillCatalog>()
            .Root<ISkillRunner>()
            .Root<IChatSearchService>()
            .Root<ITokenUsageService>()
            .Root<IHistoryCheckpointService>()
            .Root<IChatHistoryCompaction>()
            .Root<IGlobalSettingsService>()
            .Root<IChatEndpoint>()
            .Root<IChatRunDispatcher>()
            .Root<IGuideChats>()
            .Root<IChatReplySuggestions>()
            .Root<IResourceService>()
            .Root<IWorkspacePathResolver>()
            .Root<IWorkspaceFileSearch>()
            .Root<IReviewService>()
            .Root<IFilePreviewService>()
            .Root<IReviewCommentSuggestions>()
            .Root<IMemoryService>()
            .Root<IProjectInstructionsService>()
            .Root<IStandingInstructions>()
            .Root<IChatBranchIds>()
            .Root<IToolSessionFactory>()
            .Root<IExternalToolSessionFactory>()
            .Root<IAppNavigationSignal>()
            .Root<IRunEventsPublisher>()
            .Root<IBrowserAccessService>()
            .Root<IInstalledDesktop>()
            .Root<IHostUpdateService>();
}
