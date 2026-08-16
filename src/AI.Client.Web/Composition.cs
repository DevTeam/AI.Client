// ReSharper disable InconsistentNaming

// ReSharper disable UnusedMember.Local
namespace AI.Client.Web;

using Chats;
using Composer;
using Layout;
using Markdown;
using Projects;
using Runs;
using Settings;
using State;
using Microsoft.AspNetCore.Components;
using Pure.DI;
using Pure.DI.MS;
using System.Diagnostics;

internal sealed partial class Composition : ServiceProviderFactory<Composition>
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .Hint(Hint.ThreadSafe, "Off")
            .Hint(Hint.OnCannotResolveContractTypeNameWildcard, "Microsoft.JSInterop.*")
            .Root<IClientMetadata>()
            .Root<IProjectApi>()
            .Root<IChatHistoryApi>()
            .Root<IMarkdownRenderer>()
            .Root<IWorkspaceLayoutService>()
            .Root<IWorkspaceStateService>()
            .Root<IGlobalSettingsApi>()
            .Root<IChatRunsApi>()
            .Root<RunStateService>()
            .Root<IChatComposerService>()
            .Singleton<ClientMetadata, SafeMarkdownRenderer, WorkspaceLayoutService, WorkspaceStateService, ChatComposerService, RunStateService>()
            .Transient<ProjectApi, ChatHistoryApi, GlobalSettingsApi, ChatRunsApi>()
            .Transient((NavigationManager navigationManager) =>
                new HttpClient { BaseAddress = new Uri(navigationManager.BaseUri) });
}
