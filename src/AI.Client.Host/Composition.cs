using AI.Client.Application.Chat;
using AI.Client.Application.Chats;
using AI.Client.Application.Projects;
using AI.Client.Application.Notifications;
using AI.Client.Application.Settings;
using AI.Client.Application.Runs;
using AI.Client.Application.Tools;
using AI.Client.Mcp.App;
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
            // Two calls on purpose: Pure.DI 2.5.4 crashes (DIE043) on DependsOn("a", "b").
            .DependsOn("AI.Client.Contracts.Composition")
            .DependsOn("AI.Client.Server.Composition")
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
            .Singleton<HostDescriptor, ChatEndpoint>();
}
