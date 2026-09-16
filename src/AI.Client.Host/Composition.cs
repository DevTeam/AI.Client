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
            // A root as well as a singleton: minimal APIs only treat a type as a service when the
            // provider says it can resolve it, and otherwise infer it as a request body.
            .Root<IAppDataChangeSignal>()
            .Singleton<HostDescriptor, ProjectStorageLocation, PhysicalTextFileSystem, ProjectStoragePaths, JsonProjectRepository,
                Uuid7IdGenerator, SystemClock, ProjectService, ChatStoragePaths, JsonChatRepository, ChatService, ChatSearchService, ChatSynchronization,
                ProtectedDataUserDataProtector, ChatCompletionSseParser,
                OpenAiCompatibleChatCompletionClient, ChatEndpoint, GlobalSettingsPaths, JsonGlobalSettingsRepository, ProtectedGlobalSecretStore,
                GlobalSettingsService, ChatRunStoragePaths, JsonChatRunRepository, ChatRunDispatcher, ChatAgent, ToolPolicyResolver, WorkspaceChangeTracker,
                AppDataChangeSignal, AppOperationLog, AppWrites, AppMcpServerHost, CompositeToolSessionFactory>()
            // Both groups are consumed as sets, so each registration is tagged to stay distinct
            // instead of the last one silently winning its contract.
            .Singleton<AppReadTool, AppChatsTool, AppRunsTool, AppProjectsTool, AppSecurityTool, AppSubtaskTool>(Tag.Unique)
            // The adapters are pure functions with no dependencies of their own, and the set is
            // what the container has to hand out: three of them share the same abstract base, so
            // registering them individually would also register them against that base and one
            // would silently override the others. The set is therefore built here, where the
            // composition decides which adapters this build ships.
            .Singleton(_ => new ToolPresentations(
            [
                new FileToolPresentationAdapter(),
                new ProcessToolPresentationAdapter(),
                new WebToolPresentationAdapter(),
                new AppReadPresentationAdapter(),
                new AppWritePresentationAdapter(),
                new AppSubtaskPresentationAdapter(),
            ]))
            .Singleton<DefaultToolSessionFactory, AppToolSessionFactory>(Tag.Unique)
            .Singleton(_ => new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
            .Transient((ProjectStorageLocation location) => location.RootDirectory);
}
