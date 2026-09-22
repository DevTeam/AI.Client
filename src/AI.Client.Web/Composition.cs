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
using Notifications;
using AI.Client.Web.Components;
using Microsoft.AspNetCore.Components;
using AI.Client.Contracts.Tools;
using AI.Client.Contracts.Settings;
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
            // `apiBaseUrl` arrives as a string through the Composition constructor (Pure.DI's
            // source generator creates it from this `Arg`). It is the only thing the Web
            // composition needs from the entry point: by the time `new Composition(apiBase)`
            // runs the value is final (either from `?api=` or from appsettings.json), and the
            // rest of the graph can pull `IApiBaseUrl` without knowing where it came from.
            .Arg<string>("apiBaseUrl")
            .Root<IApiBaseUrl>()
            .Root<IClientMetadata>()
            .Root<IProjectApi>()
            .Root<IChatHistoryApi>()
            .Root<IChatMessageDeltaMerger>()
            .Root<IMarkdownRenderer>()
            .Root<IWorkspaceLayoutService>()
            .Root<IWorkspaceStateService>()
            .Root<IGlobalSettingsApi>()
            .Root<INotificationService>()
            .Root<IChatRunsApi>()
            .Root<IRunStateService>()
            .Root<IChatComposerService>()
            .RootBind<IToolPresentations>().To<ToolPresentations>()
            .RootBind<IToolResultModelProjector>().To<ToolResultModelProjector>()
            .RootBind<IToolResultCodec>().To<ToolResultCodec>()
            .RootBind<IConnectionContextLimitsResolver>().To<ConnectionContextLimitsResolver>()
            .RootBind<IUnifiedDiffParser>().As(Lifetime.Singleton).To<UnifiedDiff>()
            .RootBind<IChatFeedProjection>().To<ChatFeed>()
            .RootBind<IRunStatusPresentation>().To<RunStatusPresentation>()
            // `ApiBaseUrl(string)` matches the constructor generated for the `Arg` above, so
            // Pure.DI wires it in automatically. Same as how `Host/Composition.cs` registers
            // its `IChatCompletionClient` lambda in `Bind<>().To(...)`.
            .Singleton<ApiBaseUrl, ClientMetadata, SafeMarkdownRenderer, WorkspaceLayoutService, WorkspaceStateService, ChatComposerService, RunStateService, NotificationService, ChatMessageDeltaMerger>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<FileToolPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<ProcessToolPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<WebToolPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppReadPresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppWritePresentationAdapter>()
            .Bind<IToolPresentationAdapter>(Tag.Unique).To<AppSubtaskPresentationAdapter>()
            // Should be last
            .Bind<IToolPresentationAdapter>().As(Lifetime.Singleton).To<GenericToolPresentationAdapter>()
            .Transient<ProjectApi, ChatHistoryApi, GlobalSettingsApi, ChatRunsApi>()
            .Transient((IApiBaseUrl arg) => new HttpClient { BaseAddress = arg.Value });
}
