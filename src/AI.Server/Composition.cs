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
using Application.Tools;
using Application.Workspace;
using AI.Contracts.Workspace;
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
using Microsoft.Extensions.Logging;
using Mcp.App;
using Pure.DI;

/// <summary>
/// Everything the server side of AI is made of, including the HTTP API and its roots.
/// Nothing is generated here: each executable that hosts the server (the standalone Host, later
/// the desktop app) and the integration tests link this file and build on it with
/// <c>DependsOn("AI.Server.Composition")</c>, adding only their own entry-point types.
/// </summary>
internal sealed class Composition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup(kind: CompositionKind.Internal)
            // Pure.DI 2.5.4 miscompiles lightweight anonymous roots for this graph: a singleton
            // shared by several deferred factories (AppDataChangeSignal, via AppWrites) is used
            // before it is created, so AppWrites gets null. Off until that is fixed upstream.
            .Hint(Hint.LightweightAnonymousRoot, "Off")
            // What the entry point decided before starting the server: its command line, parsed.
            .Arg<ServerOptions>("options")
            // Roots are what ASP.NET can resolve: the services endpoint handlers take as parameters
            // and the hosted service's dependencies.
            .Root<IAiClientServer>("Server")
            .Root<IHostDescriptor>()
            .Root<IProjectRepository>()
            .Root<IDirectoryBrowser>()
            .Root<IProjectService>()
            .Root<IChatService>()
            .Root<IChatSearchService>()
            .Root<IChatCompletionClient>()
            .Root<IGlobalSettingsService>()
            .Root<IChatEndpoint>()
            .Root<IChatRunRepository>()
            .Root<IChatRunDispatcher>()
            .Root<IResourceService>()
            .Root<IResourceRepository>()
            .Root<IWorkspacePathResolver>()
            .Root<IReviewService>()
            .Root<IReviewRepository>()
            .Root<IMemoryService>()
            .Root<IProjectInstructionsService>()
            .Root<IStandingInstructions>()
            .Root<IUnifiedDiffParser>()
            .Root<IChatBranchIds>()
            .Root<IToolUserInterface>()
            .Root<IAppToolReply>()
            .Root<IToolSessionFactory>()
            .Root<IAppDataChangeSignal>()
            .Root<IRunEventsPublisher>()
            // Hosting
            .Singleton<AiClientServer, ApiExceptionHandler, WebClientHost, HostDescriptor, ChatEndpoint, RunEventsPublisher, RunSnapshotComparer>()
            .Singleton<HealthEndpoints, RunEndpoints, ChatEndpoints, ProjectEndpoints, SettingsEndpoints, ChatCompletionEndpoints, FileSystemEndpoints, MemoryEndpoints>(Tag.Unique)
            // One storage root for data and logs: every path below and the file logger read it here.
            .Singleton<ProjectStorageLocation, DataDirectoryLock>()
            .Bind<ILoggerProvider>().As(Lifetime.Singleton).To((IProjectStorageLocation location) => new JsonLineFileLoggerProvider(location))
            .Bind<IProjectStoragePaths>().As(Lifetime.Singleton).To((IProjectStorageLocation location) => new ProjectStoragePaths(location.RootDirectory))
            .Bind<IChatStoragePaths>().As(Lifetime.Singleton).To((IProjectStorageLocation location) => new ChatStoragePaths(location.RootDirectory))
            .Bind<IChatRunStoragePaths>().As(Lifetime.Singleton).To((IProjectStorageLocation location) => new ChatRunStoragePaths(location.RootDirectory))
            .Bind<IGlobalSettingsPaths>().As(Lifetime.Singleton).To((IProjectStorageLocation location) => new GlobalSettingsPaths(location.RootDirectory))
            // Credentials: DPAPI on Windows; elsewhere AES-GCM under a key in the system keyring,
            // or in the data directory when the machine has no working keyring.
            .Bind<IUserDataProtector>().As(Lifetime.Singleton).To<IUserDataProtector>(ctx =>
            {
                if (OperatingSystem.IsWindows())
                {
                    ctx.Inject<ProtectedDataUserDataProtector>(out var windows);
                    return windows;
                }

                ctx.Inject<MasterKeyUserDataProtector>(out var portable);
                return portable;
            })
            .Bind<IKeyringMasterKeyStore>().As(Lifetime.Singleton).To<IKeyringMasterKeyStore>(ctx =>
            {
                if (OperatingSystem.IsMacOS())
                {
                    ctx.Inject<KeychainMasterKeyStore>(out var keychain);
                    return keychain;
                }

                ctx.Inject<SecretServiceMasterKeyStore>(out var secretService);
                return secretService;
            })
            .Bind<IMasterKeyStore>().As(Lifetime.Singleton).To<KeyringOrFileMasterKeyStore>()
            .Bind<IFileMasterKeyStore>().As(Lifetime.Singleton).To<FileMasterKeyStore>()
            .Singleton<MasterKeyFormat, ProcessCommandRunner>()
            // Application and infrastructure
            .Singleton<PhysicalTextFileSystem, PhysicalDirectoryBrowser, JsonProjectRepository, ProjectDocumentSerializer,
                Uuid7IdGenerator, SystemClock, ProjectService, JsonChatRepository, ChatDocumentSerializer, ChatService, ChatSearchService, ChatSynchronization,
                ChatCompletionSseParser, ContextPlanDiagnostics, ChatTransportPolicy, ChatTransportActivity,
                JsonGlobalSettingsRepository, ProtectedGlobalSecretStore, ResourceService, ResourceModelProjection, JsonResourceRepository, ProjectPathAccess, WorkspacePathResolver,
                ReviewService, JsonReviewRepository,
                MemoryService, JsonMemoryRepository, ProjectInstructionsService, JsonProjectInstructionsRepository,
                WorkspaceInstructionFileReader, StandingInstructions,
                GlobalSettingsService, OpenAiCompatibleConnectionModelsResolver, JsonChatRunRepository, ChatRunDispatcher, ChatContext, ChatAgent, ContextTokenEstimator,
                ChatContextCompactor, ChatContextPlanner, ModelContentCheckpointService,
                ModelInstructionRegistry, ModelInstructionComposer, ToolDefinitionSelector, ToolSelectionPriorityPolicy, ToolSearchDefinitionEnricher, RunCompletionProtocol,
                ToolPolicyResolver, ToolCatalogRegistry, WorkspaceChangeTracker, LineDiff,
                AppDataChangeSignal, AppOperationLog, AppWrites, AppMcpServerHost, CompositeToolSessionFactory, ChatBranchIds, ToolUserInterface>()
            .Bind<IAppToolReply>().To<AppToolReply>()
            .Singleton<OpenAiCompatibleChatCompletionClient>("base")
            .Bind<IChatCompletionClient>().As(Lifetime.Singleton).To(([Tag("base")] IChatCompletionClient baseClient, ILogger<RetryingChatCompletionClient> retryLogger, IChatTransportPolicy transportPolicy, IChatTransportActivity transportActivity) => new RetryingChatCompletionClient(baseClient, retryLogger, transportPolicy, transportActivity))
            .Singleton<AppReadTool, AppChatsTool, AppRunsTool, AppProjectsTool, AppSecurityTool, AppSubtaskTool, AppAskUserTool, AppToolSearchTool, AppContextCompactTool, AppResourcesTool, AppMemoryTool, AppInstructionsTool>(Tag.Unique)
            .Singleton<DefaultToolSessionFactory, AppToolSessionFactory>(Tag.Unique)
            .Singleton(_ => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
}
