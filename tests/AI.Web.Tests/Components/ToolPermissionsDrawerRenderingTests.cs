namespace AI.Web.Tests.Components;

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
    private static readonly Guid ChatId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task ProjectDrawerShouldAddAndChangeItsOwnCommandRules()
    {
        IReadOnlyList<CommandRule> rules =
        [
            new(Guid.CreateVersion7(), CommandRule.Project, ProjectId, null, "git status *", "Ask", Now),
            new(Guid.CreateVersion7(), CommandRule.Global, null, null, "git push --force *", "Deny", Now)
        ];

        var html = await RenderDrawerAsync("Project", null, rules);

        html.ShouldContain("<code>git status *</code>");
        html.ShouldContain("aria-label=\"Decision for git status *\"");
        html.ShouldContain("<option value=\"Ask\" selected");
        // The Overrides tab lists the drawer's own scope only.
        html.ShouldNotContain("git push --force *");
        html.ShouldContain("class=\"command-rule-form\"");
        html.ShouldContain(">This project</option>");
        html.ShouldContain("value=\"Global\">Every project</option>");
        html.ShouldContain("class=\"command-rule-hint\"");
    }

    [Fact]
    public async Task ProjectDrawerShouldOfferTheFormBeforeAnyRuleExists()
    {
        var html = await RenderDrawerAsync("Project", null, []);

        html.ShouldContain("Command rules");
        html.ShouldContain("class=\"command-rule-form\"");
    }

    [Theory]
    [InlineData("Chat")]
    [InlineData("Branch")]
    public async Task ChatDrawerShouldOnlyShowAndResetItsCommandRules(string scope)
    {
        IReadOnlyList<CommandRule> rules =
            [new(Guid.CreateVersion7(), CommandRule.Chat, ProjectId, ChatId, "git log *", "Allow", Now)];

        var html = await RenderDrawerAsync(scope, ChatId, rules);

        html.ShouldContain("<code>git log *</code>");
        html.ShouldContain("class=\"permission-decision\"");
        html.ShouldContain("aria-label=\"Remove command rule git log *\"");
        html.ShouldNotContain("Decision for git log *");
        html.ShouldNotContain("command-rule-form");
    }

    [Fact]
    public async Task ChatDrawerShouldLeaveOutCommandRulesWhenItHasNone()
    {
        var html = await RenderDrawerAsync("Chat", ChatId, []);

        html.ShouldNotContain("Command rules");
    }

    [Fact]
    public async Task GlobalToolSettingsShouldListChangeAndAddGlobalCommandRules()
    {
        IReadOnlyList<CommandRule> rules =
            [new(Guid.CreateVersion7(), CommandRule.Global, null, null, "git push --force *", "Deny", Now)];

        var html = await RenderAsync<GlobalCommandRules>(Services(rules, null, null), []);

        html.ShouldContain("Command rules");
        html.ShouldContain("aria-label=\"Decision for git push --force *\"");
        html.ShouldContain("<option value=\"Deny\" selected");
        html.ShouldContain("aria-label=\"Remove command rule git push --force *\"");
        html.ShouldContain("class=\"command-rule-form\"");
        // One scope: nothing to choose.
        html.ShouldNotContain("Where the new command rule applies");
    }

    private static Task<string> RenderDrawerAsync(string scope, Guid? chatId, IReadOnlyList<CommandRule> rules) =>
        RenderAsync<ToolPermissionsDrawer>(Services(rules, ProjectId, chatId), new Dictionary<string, object?>
        {
            [nameof(ToolPermissionsDrawer.Scope)] = scope,
            [nameof(ToolPermissionsDrawer.ProjectId)] = ProjectId,
            [nameof(ToolPermissionsDrawer.ChatId)] = chatId,
            [nameof(ToolPermissionsDrawer.BranchId)] = scope == "Branch" ? Guid.CreateVersion7() : null,
            [nameof(ToolPermissionsDrawer.Title)] = "Title"
        });

    private static ServiceProvider Services(IReadOnlyList<CommandRule> rules, Guid? projectId, Guid? chatId)
    {
        var settings = new Mock<IGlobalSettingsApi>();
        settings.Setup(api => api.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalSettings([], [DefaultMcpServer.Settings], []));
        settings.Setup(api => api.GetDefaultToolsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        settings.Setup(api => api.GetCommandRulesAsync(projectId, chatId, It.IsAny<CancellationToken>())).ReturnsAsync(rules);
        var projects = new Mock<IProjectApi>();
        projects.Setup(api => api.GetAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectDetails(ProjectId, "Project", "", Now, Now, 1, [], [], []));
        return new ServiceCollection()
            .AddTransient<AI.Contracts.Navigation.IAppNavigationTargets, AI.Contracts.Navigation.AppNavigationTargets>()
            .AddTransient<IAppControlHints, AppControlHints>()
            .AddSingleton(settings.Object)
            .AddSingleton(projects.Object)
            .AddSingleton(Mock.Of<IChatHistoryApi>())
            .AddSingleton(Mock.Of<INotificationService>())
            .AddTransient<IToolDefaultDecision, ToolDefaultDecision>()
            .BuildServiceProvider();
    }

    private static async Task<string> RenderAsync<TComponent>(ServiceProvider services, Dictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        await using (services)
        await using (var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance))
            return await renderer.Dispatcher.InvokeAsync(async () =>
                (await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters))).ToHtmlString());
    }
}
