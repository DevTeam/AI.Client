// ReSharper disable InconsistentNaming

// ReSharper disable UnusedMember.Local
namespace AI.Web;

using Chats;
using Composer;
using FileSystem;
using AI.Contracts.Workspace;
using Layout;
using Markdown;
using Projects;
using Runs;
using Resources;
using Memory;
using Skills;
using Settings;
using State;
using Notifications;
using AI.Web.Components;
using Microsoft.AspNetCore.Components;
using AI.Contracts.Tools;
using AI.Contracts.Settings;
using Pure.DI;
using Pure.DI.MS;
using System.Diagnostics;
using Microsoft.JSInterop;

internal sealed partial class Composition : ServiceProviderFactory<Composition>
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .DependsOn("AI.Contracts.Composition")
            .Hint(Hint.ThreadSafe, "Off")
            .Hint(Hint.OnCannotResolveContractTypeNameWildcard, "Microsoft.JSInterop.*")
            .Arg<string>("apiBaseUrl")
            .Arg<bool>("publicWeb")
            .Root<HttpClient>()
            .Root<IApiBaseUrl>()
            .Root<IClientMode>()
            .Root<IHostConnection>()
            .Root<IClientMetadata>()
            .Root<IProjectApi>()
            .Root<IFileSystemApi>()
            .Root<IChatHistoryApi>()
            .Root<IChatMessageDeltaMerger>()
            .Root<IMarkdownRenderer>()
            .Root<IWorkspaceLayoutService>()
            .Root<IWorkspaceStateService>()
            .Root<IGlobalSettingsApi>()
            .Root<IClientSettingsService>()
            .Root<IThemeService>()
            .Root<INotificationService>()
            .Root<IChatRunsApi>()
            .Root<IResourceApi>()
            .Root<IReviewApi>()
            .Root<IMemoryApi>()
            .Root<ISkillApi>()
            .Root<IRunStateService>()
            .Root<IChatComposerService>()
            .Root<IToolPresentations>()
            .Root<IToolResultModelProjector>()
            .Root<IToolResultCodec>()
            .Root<IConnectionContextLimitsResolver>()
            .Root<IUnifiedDiffParser>()
            .RootBind<IDropAccessPlanner>().To<DropAccessPlanner>()
            .RootBind<IChatFeedProjection>().To<ChatFeed>()
            .RootBind<ITurnLiveText>().To<TurnLiveText>()
            .RootBind<IDirectoryPickerState>().To<DirectoryPickerState>()
            .RootBind<ISettingsTransferCodec>().To<SettingsTransferCodec>()
            .RootBind<ISettingsImportPlanner>().To<SettingsImportPlanner>()
            .RootBind<IRunStatusPresentation>().To<RunStatusPresentation>()
            .RootBind<IComposerHistoryNavigator>().To<ComposerHistoryNavigator>()
            .RootBind<IReplySuggestionState>().To<ReplySuggestionState>()
            .Bind<IChatTipsState>().As(Lifetime.Singleton).To<ChatTipsState>()
            .Root<IChatTipsState>()
            .RootBind<ISkillCommandMatcher>().To<SkillCommandMatcher>()
            .RootBind<IResourceMentionMatcher>().To<ResourceMentionMatcher>()
            .RootBind<IMentionLinkWriter>().To<MentionLinkWriter>()
            .RootBind<IResourcePresenter>().To<ResourcePresenter>()
            .Bind<IDiffSnapshotReader>().To<DiffSnapshotReader>()
            .RootBind<IComposerContextPresentation>().To<ComposerContextPresentation>()
            .RootBind<IDelayedBusyIndicatorFactory>().To<DelayedBusyIndicatorFactory>()
            .Bind<INotificationService>("base").As(Lifetime.Singleton).To<NotificationService>()
            .Singleton<DesktopBadgeNotificationService, DesktopUnreadCountPublisher, ApiBaseUrl, ClientMode, HostConnection, ClientMetadata, SafeMarkdownRenderer, WorkspaceLayoutService,
                WorkspaceStateService, ChatComposerService, RunStateService, ChatMessageDeltaMerger, ClientSettingsService, ThemeService>()
            .Transient<ProjectApi, ChatHistoryApi, GlobalSettingsApi, ChatRunsApi, FileSystemApi, ResourceApi, ReviewApi, MemoryApi, SkillApi>()
            .Transient((IApiBaseUrl arg, IClientMode mode, IJSRuntime jsRuntime) =>
                new HttpClient(new BridgeAuthorizationHandler(mode, jsRuntime)
                {
                    InnerHandler = new HttpClientHandler()
                }) { BaseAddress = arg.Value });
}
