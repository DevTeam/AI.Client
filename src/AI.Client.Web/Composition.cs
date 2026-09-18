// ReSharper disable InconsistentNaming

// ReSharper disable UnusedMember.Local
namespace AI.Client.Web;

using Chats;
using Composer;
using AI.Client.Contracts.Workspace;
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
            .RootBind<IToolPresentations>().To<ToolPresentations>()
            .RootBind<IToolResultModelProjector>().To<ToolResultModelProjector>()
            .RootBind<IToolResultCodec>().To<ToolResultCodec>()
            .RootBind<IUnifiedDiffParser>().As(Lifetime.Singleton).To<UnifiedDiff>()
            .Singleton<ClientMetadata, SafeMarkdownRenderer, WorkspaceLayoutService, WorkspaceStateService, ChatComposerService, RunStateService>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<FileToolPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<ProcessToolPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<WebToolPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppReadPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppWritePresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppSubtaskPresentationAdapter>()
            // Should be last
            .Bind<IToolPresentationAdapter>().As(Lifetime.Singleton).To<GenericToolPresentationAdapter>()
            .Transient<ProjectApi, ChatHistoryApi, GlobalSettingsApi, ChatRunsApi>()
            .Transient((NavigationManager navigationManager) =>
                new HttpClient { BaseAddress = new Uri(navigationManager.BaseUri) });
}
