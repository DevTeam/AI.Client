using AI.Client.Application.Chat;
using AI.Client.Application.Chats;
using AI.Client.Application.Projects;
using AI.Client.Application.Notifications;
using AI.Client.Application.Settings;
using AI.Client.Application.Runs;
using AI.Client.Application.Tools;
using AI.Client.Infrastructure.Tools;
using AI.Client.Mcp.App;
using AI.Client.Infrastructure.Chat;
using AI.Client.Infrastructure.Credentials;
using AI.Client.Infrastructure.Projects;
using AI.Client.Infrastructure.Storage;
using AI.Client.Infrastructure.Settings;
using AI.Client.Infrastructure.Workspace;
using AI.Client.Contracts.Tools;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Pure.DI;
using Pure.DI.MS;
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Local

namespace AI.Client.Host;

internal sealed partial class Composition : ServiceProviderFactory<Composition>
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            // The storage root arrives from the entry point, which also hands it to the file logger,
            // so one instance decides where the application writes instead of two independently
            // resolved ones that merely happen to agree.
            .Arg<string>("rootDirectory")
            .Root<IHostDescriptor>()
            .Root<IProjectRepository>()
            .Root<IProjectService>()
            .Root<IChatService>()
            .Root<IChatSearchService>()
            .Root<IChatCompletionClient>()
            .Root<IGlobalSettingsService>()
            .Root<IChatEndpoint>()
            .Root<IChatRunRepository>()
            .Root<IChatRunDispatcher>()
            .Root<IToolSessionFactory>()
            .Root<IAppDataChangeSignal>()
            .Singleton<HostDescriptor, PhysicalTextFileSystem, JsonProjectRepository,
                Uuid7IdGenerator, SystemClock, ProjectService, JsonChatRepository, ChatService, ChatSearchService, ChatSynchronization,
                ProtectedDataUserDataProtector, ChatCompletionSseParser,
                ChatEndpoint, JsonGlobalSettingsRepository, ProtectedGlobalSecretStore,
                GlobalSettingsService, JsonChatRunRepository, ChatRunDispatcher, ChatAgent, ToolPolicyResolver, WorkspaceChangeTracker,
                AppDataChangeSignal, AppOperationLog, AppWrites, AppMcpServerHost, CompositeToolSessionFactory, ToolPresentations>()
            .Singleton<OpenAiCompatibleChatCompletionClient>("base")
            .Bind<IChatCompletionClient>().As(Lifetime.Singleton).To(([Tag("base")] IChatCompletionClient baseClient, ILogger<RetryingChatCompletionClient> retryLogger) => new RetryingChatCompletionClient(baseClient, retryLogger))
            .Singleton<ToolResultModelProjector, ToolResultCodec, ProjectStoragePaths, ChatStoragePaths, ChatRunStoragePaths, GlobalSettingsPaths>()
            .Singleton<AppReadTool, AppChatsTool, AppRunsTool, AppProjectsTool, AppSecurityTool, AppSubtaskTool, AppAskUserTool>(Tag.Unique)
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<FileToolPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<ProcessToolPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<WebToolPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppReadPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppWritePresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppSubtaskPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AskUserPresentationAdapter>()
            // Should be last
            .Bind<IToolPresentationAdapter>().As(Lifetime.Singleton).To<GenericToolPresentationAdapter>()
            .Singleton<DefaultToolSessionFactory, AppToolSessionFactory>(Tag.Unique)
            .Singleton(_ => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
}
