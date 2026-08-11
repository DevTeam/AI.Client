using AI.Client.Application.Chat;
using AI.Client.Application.Chats;
using AI.Client.Application.Projects;
using AI.Client.Application.Settings;
using AI.Client.Application.Runs;
using AI.Client.Infrastructure.Chat;
using AI.Client.Infrastructure.Credentials;
using AI.Client.Infrastructure.Projects;
using AI.Client.Infrastructure.Storage;
using AI.Client.Infrastructure.Settings;
using System.Diagnostics;
using Pure.DI;
using Pure.DI.MS;
// ReSharper disable InconsistentNaming

namespace AI.Client.Host;

internal sealed partial class Composition : ServiceProviderFactory<Composition>
{
    [Conditional("DI")]
    private static void SetupDI() =>
        DI.Setup()
            .Root<IHostDescriptor>()
            .Root<IProjectRepository>()
            .Root<IProjectService>()
            .Root<IChatService>()
            .Root<IEndpointCredentialStore>()
            .Root<IChatCompletionClient>()
            .Root<IGlobalSettingsService>()
            .Root<IChatEndpoint>()
            .Root<IChatRunRepository>()
            .Root<IChatRunDispatcher>()
            .Singleton<HostDescriptor, ProjectStorageLocation, PhysicalTextFileSystem, ProjectStoragePaths, ProjectDocumentSerializer, JsonProjectRepository,
                Uuid7ProjectIdGenerator, SystemClock, ProjectService, ChatStoragePaths, ChatDocumentSerializer, JsonChatRepository, ChatService, EndpointCredentialPaths, ProtectedDataUserDataProtector, ProtectedEndpointCredentialStore,
                ChatCompletionSseParser, OpenAiCompatibleChatCompletionClient, ChatEndpoint,
                GlobalSettingsPaths, JsonGlobalSettingsRepository, ProtectedGlobalSecretStore, GlobalSettingsService,
                ChatRunStoragePaths, JsonChatRunRepository, ChatRunDispatcher>()
            .Singleton(_ => new HttpClient())
            .Transient((ProjectStorageLocation location) => location.RootDirectory);
}
