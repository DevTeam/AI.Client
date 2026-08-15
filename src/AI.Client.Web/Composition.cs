using AI.Client.Web.Chat;
using AI.Client.Web.Chats;
using AI.Client.Web.Projects;
using AI.Client.Web.Markdown;
using AI.Client.Web.Layout;
using AI.Client.Web.Settings;
using AI.Client.Web.Runs;
using AI.Client.Web.State;
using Microsoft.AspNetCore.Components;
using System.Diagnostics;
using Pure.DI;
using Pure.DI.MS;
// ReSharper disable InconsistentNaming

namespace AI.Client.Web;

internal sealed partial class Composition : ServiceProviderFactory<Composition>
{
    [Conditional("DI")]
    private static void SetupDI() =>
        DI.Setup()
            .Hint(Hint.ThreadSafe, "Off")
            .Hint(Hint.OnCannotResolveContractTypeNameWildcard, "Microsoft.JSInterop.*")
            .Root<IClientMetadata>()
            .Root<IProjectApi>()
            .Root<IChatApi>()
            .Root<IChatHistoryApi>()
            .Root<IMarkdownRenderer>()
            .Root<IWorkspaceLayoutService>()
            .Root<IWorkspaceStateService>()
            .Root<IGlobalSettingsApi>()
            .Root<IChatRunsApi>()
            .Singleton<ClientMetadata>()
            .Singleton<SafeMarkdownRenderer, WorkspaceLayoutService, WorkspaceStateService>()
            .Transient<ProjectApi, ChatApi, ChatHistoryApi, GlobalSettingsApi, ChatRunsApi>()
            .Transient((NavigationManager navigationManager) =>
                new HttpClient { BaseAddress = new Uri(navigationManager.BaseUri) });
}
