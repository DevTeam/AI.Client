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
using Infrastructure.Chat;
using Infrastructure.Credentials;
using Infrastructure.Projects;
using Infrastructure.Settings;
using Infrastructure.Storage;
using Infrastructure.Tools;
using Infrastructure.Workspace;
using Microsoft.Extensions.Logging;
using Mcp.App;
using Pure.DI;

/// <summary>
/// Everything the server side of AI.Client is made of. Nothing is generated here: each
/// executable that hosts the server (the standalone Host, later the desktop app) and the
/// integration tests link this file and build on it with
/// <c>DependsOn("AI.Client.Server.Composition")</c>, adding only their own entry-point types.
/// </summary>
internal sealed class Composition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup(kind: CompositionKind.Internal)
            // The storage root arrives from the entry point, which also hands it to the file logger,
            // so one instance decides where the application writes instead of two independently
            // resolved ones that merely happen to agree.
            .Arg<string>("rootDirectory")
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
            .Singleton<ProjectStoragePaths, ChatStoragePaths, ChatRunStoragePaths, GlobalSettingsPaths>()
            .Singleton<AppReadTool, AppChatsTool, AppRunsTool, AppProjectsTool, AppSecurityTool, AppSubtaskTool, AppAskUserTool, AppToolSearchTool, AppContextCompactTool>(Tag.Unique)
            .Singleton<DefaultToolSessionFactory, AppToolSessionFactory>(Tag.Unique)
            .Singleton(_ => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
}
