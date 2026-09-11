using AI.Client.Application.Chat;
using AI.Client.Application.Chats;
using AI.Client.Application.Projects;
using AI.Client.Application.Settings;
using AI.Client.Application.Runs;
using AI.Client.Application.Tools;
using AI.Client.Infrastructure.Tools;
using AI.Client.Infrastructure.Chat;
using AI.Client.Infrastructure.Credentials;
using AI.Client.Infrastructure.Projects;
using AI.Client.Infrastructure.Storage;
using AI.Client.Infrastructure.Settings;
using AI.Client.Infrastructure.Workspace;
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
            .Root<IChatCompletionClient>()
            .Root<IGlobalSettingsService>()
            .Root<IChatEndpoint>()
            .Root<IChatRunRepository>()
            .Root<IChatRunDispatcher>()
            .Root<IToolSessionFactory>()
            .Singleton<HostDescriptor, ProjectStorageLocation, PhysicalTextFileSystem, ProjectStoragePaths, JsonProjectRepository,
                Uuid7IdGenerator, SystemClock, ProjectService, ChatStoragePaths, JsonChatRepository, ChatService, ChatSynchronization,
                ProtectedDataUserDataProtector, ChatCompletionSseParser,
                OpenAiCompatibleChatCompletionClient, ChatEndpoint, GlobalSettingsPaths, JsonGlobalSettingsRepository, ProtectedGlobalSecretStore,
                GlobalSettingsService, ChatRunStoragePaths, JsonChatRunRepository, ChatRunDispatcher, ChatAgent, DefaultToolSessionFactory, WorkspaceChangeTracker>()
            .Singleton(_ => new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
            .Transient((ProjectStorageLocation location) => location.RootDirectory);
}
