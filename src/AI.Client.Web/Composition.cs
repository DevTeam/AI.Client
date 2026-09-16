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
using AI.Client.Contracts.Tools;
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
            .Root<IRunStateService>()
            .Root<IChatComposerService>()
            .Root<ToolPresentations>()
            .Singleton<ClientMetadata, SafeMarkdownRenderer, WorkspaceLayoutService, WorkspaceStateService, ChatComposerService, RunStateService>()
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
            .Transient<ProjectApi, ChatHistoryApi, GlobalSettingsApi, ChatRunsApi>()
            .Transient((NavigationManager navigationManager) =>
                new HttpClient { BaseAddress = new Uri(navigationManager.BaseUri) });
}
