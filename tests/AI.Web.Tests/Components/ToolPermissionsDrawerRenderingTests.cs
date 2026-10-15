namespace AI.Web.Tests.Components;

using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Settings;
using AI.Web.Chats;
using AI.Web.Components;
using AI.Web.Navigation;
using AI.Web.Notifications;
using AI.Web.Projects;
using AI.Web.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

public sealed class ToolPermissionsDrawerRenderingTests
{
    private static readonly Guid ProjectId = Guid.CreateVersion7();

    [Fact]
    public async Task ShouldOfferToAddAndChangeCommandRulesOfItsScope()
    {
        var now = DateTimeOffset.UtcNow;
        IReadOnlyList<CommandRule> rules =
        [
            new(Guid.CreateVersion7(), CommandRule.Project, ProjectId, null, "git status *", "Ask", now),
            new(Guid.CreateVersion7(), CommandRule.Global, null, null, "git push --force *", "Deny", now)
        ];

        var html = await RenderAsync(rules);

        html.ShouldContain("<code>git status *</code>");
        html.ShouldContain("aria-label=\"Decision for git status *\"");
        html.ShouldContain("<option value=\"Ask\" selected");
        // The Overrides tab lists the drawer's own scope only.
        html.ShouldNotContain("git push --force *");
        html.ShouldContain("class=\"command-rule-form\"");
        html.ShouldContain("aria-label=\"Command pattern\"");
        html.ShouldContain(">This project</option>");
        html.ShouldContain("value=\"Global\">Every project</option>");
        html.ShouldContain("class=\"command-rule-hint\"");
    }

    [Fact]
    public async Task ShouldOfferTheFormWhenThereAreNoRulesYet()
    {
        var html = await RenderAsync([]);

        html.ShouldContain("Command rules");
        html.ShouldContain("class=\"command-rule-form\"");
    }

    private static async Task<string> RenderAsync(IReadOnlyList<CommandRule> rules)
    {
        var now = DateTimeOffset.UtcNow;
        var settings = new Mock<IGlobalSettingsApi>();
        settings.Setup(api => api.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalSettings([], [DefaultMcpServer.Settings], []));
        settings.Setup(api => api.GetDefaultToolsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        settings.Setup(api => api.GetCommandRulesAsync(ProjectId, null, It.IsAny<CancellationToken>())).ReturnsAsync(rules);
        var projects = new Mock<IProjectApi>();
        projects.Setup(api => api.GetAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectDetails(ProjectId, "Project", "", now, now, 1, [], [], []));
        await using var services = new ServiceCollection()
            .AddTransient<AI.Contracts.Navigation.IAppNavigationTargets, AI.Contracts.Navigation.AppNavigationTargets>()
            .AddTransient<IAppControlHints, AppControlHints>()
            .AddSingleton(settings.Object)
            .AddSingleton(projects.Object)
            .AddSingleton(Mock.Of<IChatHistoryApi>())
            .AddSingleton(Mock.Of<INotificationService>())
            .AddTransient<IToolDefaultDecision, ToolDefaultDecision>()
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<ToolPermissionsDrawer>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ToolPermissionsDrawer.Scope)] = "Project",
                [nameof(ToolPermissionsDrawer.ProjectId)] = ProjectId,
                [nameof(ToolPermissionsDrawer.Title)] = "Project"
            }))).ToHtmlString());
    }
}
