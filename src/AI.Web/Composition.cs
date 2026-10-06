// DI guide: [Pure.DI conventions](../../docs/30-dependency-injection.md).
// ReSharper disable InconsistentNaming

// ReSharper disable UnusedMember.Local
namespace AI.Web;

using Chats;
using Composer;
using FileSystem;
using Git;
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
using Navigation;
using Usage;
using Widgets;
using Updates;
using Pure.DI.MS;
using System.Diagnostics;
using Microsoft.JSInterop;

internal sealed partial class Composition : ServiceProviderFactory<Composition>
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .DependsOn("AI.Contracts.Composition")
            .DependsOn("AI.TextCorrection.Configuration.TextCorrectionComposition")
            .Hint(Hint.Comments, "Off")
            .Hint(Hint.ThreadSafe, "Off")
            .Hint(Hint.OnCannotResolveContractTypeNameWildcard, "Microsoft.JSInterop.*")
            .Root<ITextCorrectionLanguages>()
            .Root<AI.TextCorrection.ITextAutoCorrectionAnalyzer>()
            .Root<AI.TextCorrection.IWordBoundaries>()
            .Root<AI.TextCorrection.ITextCorrectionPreparation>()
            .Root<HttpClient>()
            .Root<IApiBaseUrl>()
            .Root<IClientMode>()
            .Root<IHostConnection>()
            .Root<IClientMetadata>()
            .Root<IProjectApi>()
            .Root<IGitApi>()
            .Root<IGitPickerState>()
            .Root<IChatHistoryApi>()
            .Root<IChatMessageDeltaMerger>()
            .Root<IMarkdownRenderer>()
            .Root<IWorkspaceLayoutService>()
            .Root<IWorkspaceStateService>()
            .Root<IGlobalSettingsApi>()
            .Root<IClientSettingsService>()
            .Root<IUpdateClient>()
            .Root<IThemeService>()
            .Root<INotificationService>()
            .Root<IChatRunsApi>()
            .Root<IResourceApi>()
            .Root<IFilePreviewApi>()
            .Root<IFilePreviewViewers>()
            .Root<IFileMarkdownRenderer>()
            .Root<IReviewApi>()
            .Root<IWorkspaceUndoApi>()
            .Root<IWorkspaceUndoState>()
            .Root<IMemoryApi>()
            .Root<ISkillApi>()
            .Root<IRunStateService>()
            .Root<IChatComposerService>()
            .Root<IToolPresentations>()
            .Root<IToolResultCodec>()
            .Root<IConnectionContextLimitsResolver>()
            .Root<IUnifiedDiffParser>()
            .Root<IDropAccessPlanner>()
            .Root<IChatFeedProjection>()
            .Root<ITurnLiveText>()
            .Root<IDirectoryPickerState>()
            .Root<ISettingsTransferCodec>()
            .Root<ISettingsImportPlanner>()
            .Root<IRunStatusPresentation>()
            .Root<IComposerHistoryNavigator>()
            .Root<IReplySuggestionState>()
            .Root<INavigationCues>()
            .Root<IAppNavigationLinks>()
            .Root<IAppGuideApi>()
            .Root<IAppControlHints>()
            .Root<AI.Contracts.Navigation.IAppNavigationTargets>()
            .Root<AI.Contracts.Navigation.IAppGuideTopics>()
            .Root<IChatTipsState>()
            .Root<ISkillCommandMatcher>()
            .Root<ISettingsListFilter>()
            .Root<IResourceMentionMatcher>()
            .Root<ISearchResultPresentation>()
            .Root<IMentionLinkWriter>()
            .Root<IResourcePresenter>()
            .Root<IComposerContextPresentation>()
            .Root<IUsagePresentation>()
            .Root<IChatWidgetCatalog>()
            .Root<IChatWidgetLayout>()
            .Root<IChatFileStatisticsCalculator>()
            .Root<IChatToolStatisticsCalculator>()
            .Root<IChatPerformanceCalculator>()
            .Root<IChatKnowledgeStatisticsCalculator>()
            .Root<IChatSubtaskStatisticsCalculator>()
            .Root<IChatTimelineStatisticsCalculator>()
            .Root<IChatBranchesStatisticsCalculator>()
            .Root<IChatReferenceStatisticsCalculator>()
            .Root<IChatModelStatisticsCalculator>()
            .Root<IChatUsageStore>()
            .Root<IHistoryCheckpointApi>()
            .Root<IDelayedBusyIndicatorFactory>()
            .Arg<string>("apiBaseUrl")
            .Arg<bool>("publicWeb")
            .Singleton<TextCorrectionLanguages, UpdateClient, ChatTipsState, ChatUsageStore,
                DesktopBadgeNotificationService, HostConnection, SafeMarkdownRenderer, WorkspaceLayoutService,
                WorkspaceUndoState,
                WorkspaceStateService, RunStateService, ClientSettingsService, ThemeService>()
            .Transient<GitPickerState, DropAccessPlanner, ChatFeed, TurnLiveText, DirectoryPickerState,
                SettingsTransferCodec, SettingsImportPlanner, RunStatusPresentation, ComposerHistoryNavigator,
                ReplySuggestionState, NavigationCues, AppNavigationLinks, AppGuideApi, AppControlHints,
                AI.Contracts.Navigation.AppNavigationTargets, AI.Contracts.Navigation.AppGuideTopics,
                SkillCommandMatcher, SettingsListFilter, ResourceMentionMatcher, SearchResultPresentation, MentionLinkWriter,
                ResourcePresenter, DiffSnapshotReader, ComposerContextPresentation, UsagePresentation,
                ChatWidgetCatalog, ChatWidgetLayout, ChatFileStatisticsCalculator, ChatToolStatisticsCalculator,
                ChatPerformanceCalculator, ChatKnowledgeStatisticsCalculator, ChatSubtaskStatisticsCalculator,
                ChatTimelineStatisticsCalculator, ChatBranchesStatisticsCalculator, ChatReferenceStatisticsCalculator,
                ChatModelStatisticsCalculator, ChatUsageApi, HistoryCheckpointApi,
                DelayedBusyIndicatorFactory, ApiBaseUrl, ClientMode, ClientMetadata, ChatComposerService,
                ChatMessageDeltaMerger, ProjectApi, ChatHistoryApi, GlobalSettingsApi, ChatRunsApi, FileSystemApi,
                GitApi, ResourceApi, ReviewApi, WorkspaceUndoApi, MemoryApi, SkillApi, FilePreviewApi, FilePreviewViewers,
                FileMarkdownRenderer>()
            .Transient<MediaFilePreviewRegistration, TextFilePreviewRegistration,
                DirectoryFilePreviewRegistration, ArchiveFilePreviewRegistration>(Tag.Unique)
            .Transient<NotificationService>("base")
            .Bind<IUnreadCountPublisher>().To<DesktopUnreadCountPublisher>()
            .Transient((IApiBaseUrl arg, IClientMode mode, IJSRuntime jsRuntime) =>
                new HttpClient(new BridgeAuthorizationHandler(mode, jsRuntime)
                {
                    InnerHandler = new HttpClientHandler()
                }) { BaseAddress = arg.Value });
}
