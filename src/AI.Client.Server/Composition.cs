// ReSharper disable UnusedMember.Local
namespace AI.Client.Server;

using System.Diagnostics;
using Application.Chat;
using Application.Chats;
using Application.Notifications;
using Application.Projects;
using Application.Runs;
using Application.Settings;
using Application.Tools;
using Application.Workspace;
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
/// Everything the server side of AI.Client is made of, including the HTTP API and its roots.
/// Nothing is generated here: each executable that hosts the server (the standalone Host, later
/// the desktop app) and the integration tests link this file and build on it with
/// <c>DependsOn("AI.Client.Server.Composition")</c>, adding only their own entry-point types.
/// </summary>
internal sealed class Composition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup(kind: CompositionKind.Internal)
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
            .Root<IChatBranchIds>()
            .Root<IToolUserInterface>()
            .Root<IAppToolReply>()
            .Root<IToolSessionFactory>()
            .Root<IAppDataChangeSignal>()
            .Root<IRunEventsPublisher>()
            // Hosting
            .Singleton<AiClientServer, ApiExceptionHandler, HostDescriptor, ChatEndpoint, RunEventsPublisher, RunSnapshotComparer>()
            .Singleton<HealthEndpoints, RunEndpoints, ChatEndpoints, ProjectEndpoints, SettingsEndpoints, ChatCompletionEndpoints, FileSystemEndpoints>(Tag.Unique)
            // One storage root for data and logs: every path below and the file logger read it here.
            .Singleton<ProjectStorageLocation>()
            .Bind<ILoggerProvider>().As(Lifetime.Singleton).To((IProjectStorageLocation location) => new JsonLineFileLoggerProvider(location))
            .Bind<IProjectStoragePaths>().As(Lifetime.Singleton).To((IProjectStorageLocation location) => new ProjectStoragePaths(location.RootDirectory))
            .Bind<IChatStoragePaths>().As(Lifetime.Singleton).To((IProjectStorageLocation location) => new ChatStoragePaths(location.RootDirectory))
            .Bind<IChatRunStoragePaths>().As(Lifetime.Singleton).To((IProjectStorageLocation location) => new ChatRunStoragePaths(location.RootDirectory))
            .Bind<IGlobalSettingsPaths>().As(Lifetime.Singleton).To((IProjectStorageLocation location) => new GlobalSettingsPaths(location.RootDirectory))
            // Application and infrastructure
            .Singleton<PhysicalTextFileSystem, PhysicalDirectoryBrowser, JsonProjectRepository, ProjectDocumentSerializer,
                Uuid7IdGenerator, SystemClock, ProjectService, JsonChatRepository, ChatDocumentSerializer, ChatService, ChatSearchService, ChatSynchronization,
                ProtectedDataUserDataProtector, ChatCompletionSseParser, ContextPlanDiagnostics, ChatTransportPolicy, ChatTransportActivity,
                JsonGlobalSettingsRepository, ProtectedGlobalSecretStore,
                GlobalSettingsService, JsonChatRunRepository, ChatRunDispatcher, ChatContext, ChatAgent, ContextTokenEstimator,
                ChatContextCompactor, ChatContextPlanner, ModelContentCheckpointService,
                ModelInstructionRegistry, ModelInstructionComposer, ToolDefinitionSelector, ToolSelectionPriorityPolicy, ToolSearchDefinitionEnricher, RunCompletionProtocol,
                ToolPolicyResolver, ToolCatalogRegistry, WorkspaceChangeTracker, LineDiff,
                AppDataChangeSignal, AppOperationLog, AppWrites, AppMcpServerHost, CompositeToolSessionFactory, ChatBranchIds, ToolUserInterface>()
            .Bind<IAppToolReply>().To<AppToolReply>()
            .Singleton<OpenAiCompatibleChatCompletionClient>("base")
            .Bind<IChatCompletionClient>().As(Lifetime.Singleton).To(([Tag("base")] IChatCompletionClient baseClient, ILogger<RetryingChatCompletionClient> retryLogger, IChatTransportPolicy transportPolicy, IChatTransportActivity transportActivity) => new RetryingChatCompletionClient(baseClient, retryLogger, transportPolicy, transportActivity))
            .Singleton<AppReadTool, AppChatsTool, AppRunsTool, AppProjectsTool, AppSecurityTool, AppSubtaskTool, AppAskUserTool, AppToolSearchTool, AppContextCompactTool>(Tag.Unique)
            .Singleton<DefaultToolSessionFactory, AppToolSessionFactory>(Tag.Unique)
            .Singleton(_ => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
}
