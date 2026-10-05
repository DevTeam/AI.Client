namespace AI.Web.Tests.Components;

using AI.Contracts.Chats;
using AI.Contracts.Workspace;
using AI.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using Shouldly;
using System.Text.RegularExpressions;
using Xunit;

public sealed class WorkspaceChangesRenderingTests
{
    [Fact]
    public async Task ShouldConnectEveryDisclosureToAnExistingDetailsElement()
    {
        var composition = new Composition("http://127.0.0.1:52173/", publicWeb: true);
        var registrations = new ServiceCollection();
        registrations.AddTransient<AI.Contracts.Navigation.IAppNavigationTargets, AI.Contracts.Navigation.AppNavigationTargets>();
        registrations.AddTransient<AI.Web.Navigation.IAppControlHints, AI.Web.Navigation.AppControlHints>();
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = (ServiceProvider)composition.CreateServiceProvider(composition.CreateBuilder(registrations));
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var changes = new WorkspaceChangeSet(
            [new("first.cs", FileChangeKind.Added, 1, 0), new("second.cs", FileChangeKind.Added, 1, 0)], 2, 0);

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<WorkspaceChanges>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(WorkspaceChanges.Changes)] = changes,
                    [nameof(WorkspaceChanges.InitiallyExpanded)] = true
                }));
            var html = component.ToHtmlString();
            var controlledIds = Regex.Matches(html, "aria-controls=\"([^\"]+)\"")
                .Select(match => match.Groups[1].Value).ToArray();

            controlledIds.Length.ShouldBe(3);
            controlledIds.Distinct().Count().ShouldBe(3);
            foreach (var id in controlledIds) html.ShouldContain($"id=\"{id}\"");
        });
    }

    [Fact]
    public async Task ShouldOpenAFileCommentFromTheCollapsedFileHeader()
    {
        var composition = new Composition("http://127.0.0.1:52173/", publicWeb: true);
        var registrations = new ServiceCollection();
        registrations.AddTransient<AI.Contracts.Navigation.IAppNavigationTargets, AI.Contracts.Navigation.AppNavigationTargets>();
        registrations.AddTransient<AI.Web.Navigation.IAppControlHints, AI.Web.Navigation.AppControlHints>();
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = (ServiceProvider)composition.CreateServiceProvider(composition.CreateBuilder(registrations));
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        CommentHost? host = null;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<CommentHost>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(CommentHost.Capture)] = (Action<CommentHost>)(value => host = value) }));
            var collapsed = component.ToHtmlString();
            collapsed.ShouldContain("workspace-change-comment-action");
            collapsed.ShouldNotContain("review-diff-content");

            await host!.CommentAction.InvokeAsync();

            var expanded = component.ToHtmlString();
            expanded.ShouldContain("review-diff-content");
            expanded.ShouldContain("review-comment-editor");
            expanded.ShouldNotContain("review-file-comment-action");
            host.ToggleCount.ShouldBe(0);
        });
    }

    private sealed class CommentHost : ComponentBase
    {
        private readonly FileChange _file = new("File.cs", FileChangeKind.Added, 1, 0, Diff: "@@ -0,0 +1,1 @@\n+code");
        private readonly Guid _sourceId = Guid.NewGuid();
        private bool _expanded;
        [Parameter] public Action<CommentHost> Capture { get; set; } = null!;
        public EventCallback CommentAction { get; private set; }
        public int ToggleCount { get; private set; }

        protected override void OnInitialized() => Capture(this);

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<ReviewDiffFile>(0);
            builder.AddAttribute(1, nameof(ReviewDiffFile.File), _file);
            builder.AddAttribute(2, nameof(ReviewDiffFile.SourceMessageId), _sourceId);
            builder.AddAttribute(3, nameof(ReviewDiffFile.Expanded), _expanded);
            builder.AddAttribute(4, nameof(ReviewDiffFile.OnExpand), EventCallback.Factory.Create(this, () => { _expanded = true; }));
            builder.AddAttribute(5, nameof(ReviewDiffFile.HeaderTemplate), (RenderFragment<EventCallback>)(comment => header =>
            {
                CommentAction = comment;
                header.OpenComponent<WorkspaceChangeFileHeader>(0);
                header.AddAttribute(1, nameof(WorkspaceChangeFileHeader.File), _file);
                header.AddAttribute(2, nameof(WorkspaceChangeFileHeader.Expanded), _expanded);
                header.AddAttribute(3, nameof(WorkspaceChangeFileHeader.OnComment), comment);
                header.AddAttribute(4, nameof(WorkspaceChangeFileHeader.OnToggle), EventCallback.Factory.Create(this, () =>
                {
                    ToggleCount++;
                    _expanded = !_expanded;
                }));
                header.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ShouldKeepRoundReceiptsOpeningUpwardAfterAnotherTurnStarts(bool intermediateReceipt, bool nextTurnStarted)
    {
        var composition = new Composition("http://127.0.0.1:52173/", publicWeb: true);
        var registrations = new ServiceCollection();
        registrations.AddTransient<AI.Contracts.Navigation.IAppNavigationTargets, AI.Contracts.Navigation.AppNavigationTargets>();
        registrations.AddTransient<AI.Web.Navigation.IAppControlHints, AI.Web.Navigation.AppControlHints>();
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = (ServiceProvider)composition.CreateServiceProvider(composition.CreateBuilder(registrations));
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var now = DateTimeOffset.UtcNow;
        var changes = new WorkspaceChangeSet([new("edit-test/edit_demo.md", FileChangeKind.Added, 13, 0)], 13, 0);
        List<ChatMessageView> messages = [];

        void Append(string role, WorkspaceChangeSet? receipt = null) => messages.Add(
            new(Guid.NewGuid(), messages.LastOrDefault()?.Id, role, "", now.AddSeconds(messages.Count), WorkspaceChanges: receipt));

        Append("User");
        Append("Assistant", changes);
        Append("User");
        if (intermediateReceipt) Append("Tool", changes);
        Append("Assistant", intermediateReceipt ? null : changes);
        if (nextTurnStarted) Append("User");
        var chat = new ChatDetails(Guid.NewGuid(), Guid.NewGuid(), "File changes", now, now, 1, null, messages);

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<MessageFeed>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(MessageFeed.HasSelectedProject)] = true,
                    [nameof(MessageFeed.SelectedChat)] = chat,
                    [nameof(MessageFeed.BranchLeafId)] = messages[^1].Id
                }));
            var cards = component.ToHtmlString().Split("<section class=\"workspace-changes ", StringSplitOptions.None)
                .Skip(1).Select(fragment => fragment.Split('"')[0]).ToArray();

            cards.Length.ShouldBe(2);
            cards[0].ShouldContain("opens-upward");
            cards[1].ShouldContain("opens-upward");
        });
    }
}
