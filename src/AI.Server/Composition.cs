// DI guide: [Pure.DI conventions](../../docs/30-dependency-injection.md).
// ReSharper disable UnusedMember.Local
namespace AI.Server;

using System.Diagnostics;
using Application.Chat;
using Application.Chats;
using Application.Instructions;
using Application.Memory;
using Application.Notifications;
using Application.Plans;
using Application.Projects;
using Application.Runs;
using Application.Schedules;
using Application.Resources;
using Application.Settings;
using Application.Skills;
using Application.Tools;
using Application.Tools.Commands;
using Application.Workspace;
using Application.Usage;
using Application.Users;
using AI.Contracts.FileSystem;
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
            // The file system and the path semantics every consumer here uses are bound once, in the
            // shared contract setup: the server graph must be buildable on its own, not only from an
            // executable that happens to add that setup for it.
            .DependsOn("AI.Contracts.Composition")
            .Hint(Hint.Comments, "Off")
            // What the entry point decided before starting the server: its command line, parsed.
            .Arg<ServerOptions>("options")
            // Shared state that must keep its identity across requests: storage repositories with their
            // write gates, chat synchronization, change and navigation signals, the run dispatcher,
            // usage accounting, preview tickets, parsed caches and the running update service.
            // The ambient user context is one instance so that a scope entered by a request is the
            // one every service of that request reads.
            .Singleton<AmbientUserContext>()
            .Singleton<FilePreviewService, ResourceAssetService, HostUpdateService, BrowserAccessService,
                JsonProjectRepository, JsonProjectOrderRepository, ChatSynchronization, ChatTransportActivity,
                JsonGlobalSettingsRepository, JsonResourceRepository, JsonReviewRepository, JsonMemoryRepository,
                JsonProjectInstructionsRepository, ChatRunDispatcher,
                ModelContentCheckpointService, ModelInstructionRegistry, ToolCatalogRegistry, WorkspaceChangeTracker, ChatPlans,
                WorkspaceUndoService,
                ContextEstimateSamples, AppDataChangeSignal, AppNavigationSignal, AppOperationLog, WorkspaceFileSearch,
                TokenUsageMeter, JsonLinesTokenUsageLedger, PromptPrefixTracker, UsageCostEstimator, ConnectionRateLimits,
                JsonHistoryCheckpointRepository, SkillCatalog, SkillRunner, ContextTextTokenizer, ChatScheduler, JsonCommandRuleRepository,
                ChatKindPolicyRegistry, ConnectionContextWindowDetector, JsonLineFileLoggerProvider, ChatReplySuggestions, ChatRepositoryRouter>()
            .Bind<IPersistentChatRepository>().As(Lifetime.Singleton).To<JsonChatRepository>()
            .Bind<IHostLifetimeChatRepository>().As(Lifetime.Singleton).To<HostLifetimeChatRepository>()
            .Bind<IPersistentChatRunRepository>().As(Lifetime.Singleton).To<JsonChatRunRepository>()
            .Bind<IHostLifetimeChatRunRepository>().As(Lifetime.Singleton).To<HostLifetimeChatRunRepository>()
            .Bind<IChatRunRepository>().As(Lifetime.Singleton).To<ChatRunRepositoryRouter>()
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
            // Server hosting, storage and the chat services built on them.
            .Transient<CompositeToolSessionFactory, ChatArchiveService, UpdateManagerFactory, PublishedUpdateFeed, UpdateInstaller,
                UpdateInstallationProvider, AiClientServer, ApiExceptionHandler, WebClientHost, HostDescriptor,
                ChatEndpoint, RunEventsPublisher, RunSnapshotComparer, InstalledDesktop, InstanceStorageLocation, UserStorageLocation,
                DataDirectoryLock, ProjectStoragePaths, ChatStoragePaths, ChatRunStoragePaths, GlobalSettingsPaths,
                PhysicalDirectoryBrowser, ProjectDocumentSerializer, Uuid7IdGenerator,
                SystemClock, ProjectService, ChatDocumentSerializer, ChatService, ChatSearchService, PinOrderKeys,
                ChatBranchSettingsResolver,
                ChatCompletionSseParser, ContextPlanDiagnostics, ChatTransportPolicy, ProtectedGlobalSecretStore,
                ResourceService, ResourceModelProjection, ProjectPathAccess, WorkspacePathResolver, ReviewService,
                ChatTemporaryDirectory, SystemShell,
                MemoryService, ProjectInstructionsService, FilePreviewFormats, FilePreviewTextReader,
                TextFilePreviewFormat, WorkspaceUndoGuard, ExecutableLocator, CommandParser, CommandRuleService, ToolRuleProfiles,
                WorkspaceInstructionFileReader, StandingInstructions, GlobalSettingsService,
                OpenAiCompatibleConnectionModelsResolver, ConnectionImageProbe, ChatContext, ModelMessageHeader, TeamStatusBrief,
                LocalUserDirectory, LocalUserResolver, UserScopeMiddleware>()
            // The adapters behind ADR-014's ports, in a call of their own for the same reason.
            .Transient<DirectoryBlobStore, JsonScheduleDocumentRepository, FileExtensionMediaTypes>()
            // Tools, their policies and the skills and schedules that drive them. Kept as a second
            // call because the generator declares a limited number of type parameters per overload.
            .Transient<ChatAgent, ContextTokenEstimator,
                ChatContextCompactor, ChatContextPlanner, KeyringOrFileMasterKeyStore, ModelInstructionComposer,
                AdaptiveContextPolicy, ToolResultContextProjector, ToolDiscoveryGuidance,
                ToolPolicyResolver, LineDiff, MasterKeyFormat, ProcessCommandRunner, AppWrites, AppMcpServerHost,
                ExternalToolSessionFactory, LocalToolEnvironment, ChatBranchIds, ToolUserInterface, GitWorkspaceDiffReader, GitBrowser,
                FileExcerptReader, ChatCompletionUsageReader, TokenUsageAggregator, TokenUsageService,
                RateLimitHeaderReader, ContextSummaryWriter, HistoryCheckpointService, ChatHistoryCompaction,
                ToolAutoApprover, GuideChats, ConnectionChoice, ReviewCommentSuggestions, AppToolReply,
                GenericSkillExecutor, SkillGuide, SkillRouting, SkillCandidateSelector, AppNavigationTargets, AppGuideTopics, AppGuideLanguageContext,
                ChatScheduleStore, ChatScheduleService, ScheduledChatPass, ScheduleDemo, ScheduledChatQuery>()
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
            .Transient<ConversationChatKindPolicy, GuideChatKindPolicy, DemoChatKindPolicy, TeamDemoChatKindPolicy,
                ScheduledChatKindPolicy, HealthEndpoints, RunEndpoints, ChatEndpoints, ProjectEndpoints, SettingsEndpoints,
                ChatCompletionEndpoints, FileSystemEndpoints, FilePreviewEndpoints, GitEndpoints, MemoryEndpoints,
                SkillEndpoints, BrowserAccessEndpoints, UsageEndpoints, HistoryCheckpointEndpoints, UpdateEndpoints,
                AppGuideEndpoints, ScheduleEndpoints, AppReadTool, AppChatsTool, AppRunsTool, AppProjectsTool, AppSecurityTool, AppSubtaskTool,
                AppAskUserTool, AppToolSearchTool, AppContextCompactTool, AppResourcesTool, AppMemoryTool,
                AppInstructionsTool, AppSkillSearchTool, AppSkillRunTool, AppSkillsTool, AppNavigateTool, AppScheduleTool, AppPlanTool,
                DefaultToolSessionFactory, DirectoryFilePreviewFormat, ArchiveFilePreviewFormat, MediaFilePreviewFormat,
                MarkupFilePreviewFormat>(Tag.Unique)
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
            .Root<IChatScheduleService>()
            .Root<IScheduledChatQuery>()
            .Root<ISkillCatalog>()
            .Root<ISkillRunner>()
            .Root<IChatSearchService>()
            .Root<ITokenUsageService>()
            .Root<IHistoryCheckpointService>()
            .Root<IChatHistoryCompaction>()
            .Root<IGlobalSettingsService>()
            .Root<ICommandRuleService>()
            .Root<IConnectionImageProbe>()
            .Root<IChatEndpoint>()
            .Root<IChatRunDispatcher>()
            .Root<IGuideChats>()
            .Root<IChatReplySuggestions>()
            .Root<IResourceService>()
            .Root<IWorkspacePathResolver>()
            .Root<IChatTemporaryDirectory>()
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
            .Root<IUserDirectory>()
            .Root<IUserScope>()
            .Root<IHostUpdateService>();
}
