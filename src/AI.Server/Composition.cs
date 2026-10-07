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
            // Shared state that must keep its identity across requests: storage repositories with their
            // write gates, chat synchronization, change and navigation signals, the run dispatcher,
            // usage accounting, preview tickets, parsed caches and the running update service.
            // CompositeToolSessionFactory is cached here only because Pure.DI 2.5.4 emits out-of-scope
            // factory locals for it when transient — see docs/pure-di-issues.md.
            .Singleton<FilePreviewService, ResourceAssetService, HostUpdateService, BrowserAccessService,
                JsonProjectRepository, ChatSynchronization, ChatTransportActivity,
                JsonGlobalSettingsRepository, JsonResourceRepository, JsonReviewRepository, JsonMemoryRepository,
                JsonProjectInstructionsRepository, ChatRunDispatcher,
                ModelContentCheckpointService, ModelInstructionRegistry, ToolCatalogRegistry, WorkspaceChangeTracker,
                WorkspaceUndoService,
                ContextEstimateSamples, AppDataChangeSignal, AppNavigationSignal, AppOperationLog, WorkspaceFileSearch,
                TokenUsageMeter, JsonLinesTokenUsageLedger, PromptPrefixTracker, UsageCostEstimator, ConnectionRateLimits,
                JsonHistoryCheckpointRepository, SkillCatalog, SkillRunner, CompositeToolSessionFactory, ContextTextTokenizer>()
            .Singleton<ChatKindPolicyRegistry>()
            .Bind<IPersistentChatRepository>().As(Lifetime.Singleton).To<JsonChatRepository>()
            .Bind<IHostLifetimeChatRepository>().As(Lifetime.Singleton).To<HostLifetimeChatRepository>()
            .Bind<IPersistentChatRunRepository>().As(Lifetime.Singleton).To<JsonChatRunRepository>()
            .Bind<IHostLifetimeChatRunRepository>().As(Lifetime.Singleton).To<HostLifetimeChatRunRepository>()
            .Bind<IChatRepository>().As(Lifetime.Singleton).To<ChatRepositoryRouter>()
            .Bind<IChatRunRepository>().As(Lifetime.Singleton).To<ChatRunRepositoryRouter>()
            .Singleton((IProjectStorageLocation location) => new JsonLineFileLoggerProvider(location))
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
            .Singleton(_ => new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
            // Reading embedded skill definitions once avoids repeated parsing.
            .Singleton<BuiltInSkillCatalog>("built-in")
            .Singleton<OpenAiCompatibleChatCompletionClient>("base")
            .Bind<IChatReplySuggestions>().As(Lifetime.Singleton).To<ChatReplySuggestions>()
            // Bindings: one call per lifetime and tag. Order inside a call carries no meaning; a call
            // carries at most the number of type parameters Pure.DI declares for one binding method.
            // Request handling, storage layout and workspace access.
            .Transient<ChatArchiveService, UpdateManagerFactory, GitHubUpdateFeed, UpdateInstaller,
                UpdateInstallationProvider, AiClientServer, ApiExceptionHandler, WebClientHost, HostDescriptor,
                ChatEndpoint, RunEventsPublisher, RunSnapshotComparer, InstalledDesktop, ProjectStorageLocation,
                DataDirectoryLock, ProjectStoragePaths, ChatStoragePaths, ChatRunStoragePaths, GlobalSettingsPaths,
                PhysicalTextFileSystem, PhysicalDirectoryBrowser, ProjectDocumentSerializer, Uuid7IdGenerator,
                SystemClock, ProjectService, ChatDocumentSerializer, ChatService, ChatSearchService, PinOrderKeys,
                ChatCompletionSseParser, ContextPlanDiagnostics, ChatTransportPolicy, ProtectedGlobalSecretStore,
                ResourceService, ResourceModelProjection, ProjectPathAccess, WorkspacePathResolver, ReviewService,
                MemoryService, ProjectInstructionsService, FilePreviewFormats, FilePreviewTextReader,
                TextFilePreviewFormat, WorkspaceUndoGuard>()
            .Transient<DirectoryFilePreviewFormat, ArchiveFilePreviewFormat, MediaFilePreviewFormat,
                MarkupFilePreviewFormat>(Tag.Unique)
            // Instruction composition, context planning, credentials and usage accounting.
            .Transient<WorkspaceInstructionFileReader, StandingInstructions, GlobalSettingsService,
                OpenAiCompatibleConnectionModelsResolver, ConnectionImageProbe, ChatContext, ModelMessageHeader, ChatAgent, ContextTokenEstimator,
                ChatContextCompactor, ChatContextPlanner, KeyringOrFileMasterKeyStore, ModelInstructionComposer,
                AdaptiveContextPolicy, ToolResultContextProjector, ToolDiscoveryGuidance,
                ToolPolicyResolver, LineDiff, MasterKeyFormat, ProcessCommandRunner, AppWrites, AppMcpServerHost,
                ExternalToolSessionFactory, ChatBranchIds, ToolUserInterface, GitWorkspaceDiffReader, GitBrowser,
                FileExcerptReader, ChatCompletionUsageReader, TokenUsageAggregator, TokenUsageService,
                RateLimitHeaderReader, ContextSummaryWriter, HistoryCheckpointService, ChatHistoryCompaction,
                ToolAutoApprover, GuideChats, ConnectionChoice, ReviewCommentSuggestions, AppToolReply,
                GenericSkillExecutor, SkillGuide, SkillRouting, AppNavigationTargets, AppGuideTopics, AppGuideLanguageContext>()
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
            // Executors share a contract; tags identify the role without concrete dependencies.
            .Transient<ChatRenameSkill>("chat-rename")
            .Transient<ChatReplySuggestSkill>("chat-reply-suggest")
            .Transient<ChatCommentSuggestSkill>("chat-comment-suggest")
            .Transient<SkillRouteSkill>("skill-route")
            .Transient<ChatToolRiskAssessSkill>("chat-tool-risk-assess")
            // Endpoints, app tools and session factories are injected as collections; the tag belongs to
            // the call, so each tagged group stays on its own.
            .Transient<ConversationChatKindPolicy, GuideChatKindPolicy, DemoChatKindPolicy>(Tag.Unique)
            .Transient<HealthEndpoints, RunEndpoints, ChatEndpoints, ProjectEndpoints, SettingsEndpoints,
                ChatCompletionEndpoints, FileSystemEndpoints, FilePreviewEndpoints, GitEndpoints, MemoryEndpoints,
                SkillEndpoints, BrowserAccessEndpoints, UsageEndpoints, HistoryCheckpointEndpoints, UpdateEndpoints,
                AppGuideEndpoints>(Tag.Unique)
            .Transient<AppReadTool, AppChatsTool, AppRunsTool, AppProjectsTool, AppSecurityTool, AppSubtaskTool,
                AppAskUserTool, AppToolSearchTool, AppContextCompactTool, AppResourcesTool, AppMemoryTool,
                AppInstructionsTool, AppSkillSearchTool, AppSkillRunTool, AppSkillsTool, AppNavigateTool,
                DefaultToolSessionFactory, CSharpToolSessionFactory>(Tag.Unique)
            .Transient<AppToolSessionFactory>(Tag.Type)
            .Transient((
                [Tag("base")] IChatCompletionClient baseClient,
                ILogger<RetryingChatCompletionClient> retryLogger,
                IChatTransportPolicy transportPolicy,
                IChatTransportActivity transportActivity,
                ITokenUsageMeter usageMeter,
                IContextTokenEstimator usageEstimator,
                IPromptPrefixTracker prefixes, IAdaptiveContextPolicy contextPolicy) =>
                new MeteringChatCompletionClient(
                    new RetryingChatCompletionClient(baseClient, retryLogger, transportPolicy, transportActivity),
                    usageMeter, usageEstimator, prefixes, contextPolicy));
}

/// <summary>Only the roots resolved by ASP.NET handlers and hosted services.</summary>
internal sealed class AspNetComposition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup(kind: CompositionKind.Internal)
            .DependsOn("AI.Server.Composition")
            .Hint(Hint.Comments, "Off")
            .Root<IHostDescriptor>()
            .Root<IDirectoryBrowser>()
            .Root<IGitBrowser>()
            .Root<IProjectService>()
            .Root<IChatService>()
            .Root<IChatKindPolicyRegistry>()
            .Root<IChatArchiveService>()
            .Root<ISkillCatalog>()
            .Root<ISkillRunner>()
            .Root<IChatSearchService>()
            .Root<ITokenUsageService>()
            .Root<IHistoryCheckpointService>()
            .Root<IChatHistoryCompaction>()
            .Root<IGlobalSettingsService>()
            .Root<IConnectionImageProbe>()
            .Root<IChatEndpoint>()
            .Root<IChatRunDispatcher>()
            .Root<IGuideChats>()
            .Root<IChatReplySuggestions>()
            .Root<IResourceService>()
            .Root<IWorkspacePathResolver>()
            .Root<IWorkspaceFileSearch>()
            .Root<IReviewService>()
            .Root<IWorkspaceUndoService>()
            .Root<IWorkspaceUndoGuard>()
            .Root<IFilePreviewService>()
            .Root<IResourceAssetService>()
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
