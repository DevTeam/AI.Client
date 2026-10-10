namespace AI.Web.Tests.Widgets;

using System.Net;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Schedules;
using AI.Contracts.Settings;
using AI.Web.Chats;
using AI.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using Shouldly;
using Xunit;

public sealed class BranchSettingsDialogRenderingTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ChatId = Guid.NewGuid();
    private static readonly Guid Parent = Guid.NewGuid();
    private static readonly Guid Child = Guid.NewGuid();
    private static readonly ConnectionSettings Fast = Connection("Fast");
    private static readonly ConnectionSettings Strong = Connection("Strong");

    [Fact]
    public async Task ShouldShowWhereEachInheritedValueComesFrom()
    {
        var html = await RenderAsync(Chat(new BranchSettings(Strong.Id, ToolApprovalMode.FullAccess), null), Child);

        var text = WebUtility.HtmlDecode(html);
        text.ShouldContain("Branch settings");
        text.ShouldContain("Child · follows Parent");
        text.ShouldContain("Strong · from Parent");
        text.ShouldContain("Full access · from Parent");
        text.ShouldContain("Its own; schedules are never inherited.");
        // Both settings follow the parent, so "Inherit" is the chosen line and nothing is set here.
        Count(html, "aria-checked=\"true\"").ShouldBe(2);
        html.ShouldNotContain("branch-settings-source is-own");
        html.ShouldContain("disabled");
    }

    [Fact]
    public async Task ShouldMarkValuesTheBranchSetsItself()
    {
        var html = await RenderAsync(Chat(null, new BranchSettings(Fast.Id, ToolApprovalMode.Auto,
            [new ToolPolicySettings(Guid.NewGuid(), "fs_write", "h", "Deny", null, null)])), Child);

        var text = WebUtility.HtmlDecode(html);
        Count(html, "branch-settings-source is-own").ShouldBe(2);
        text.ShouldContain("1 rule here");
        text.ShouldContain("Every weekday at 09:00");
        html.ShouldNotContain("disabled=\"\"");
    }

    [Fact]
    public async Task ShouldOfferTheProjectDefaultInsteadOfInheritanceOnTheMainBranch()
    {
        var text = WebUtility.HtmlDecode(await RenderAsync(Chat(null, null), ChatId));

        text.ShouldContain("Chat settings");
        text.ShouldContain("Every branch follows these unless it sets its own");
        text.ShouldContain("Project default");
        text.ShouldNotContain("Inherit everything");
    }

    private static ChatDetails Chat(BranchSettings? parent, BranchSettings? child) => new(ChatId, ProjectId, "Chat",
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 3, null, [],
        [
            new ChatBranchView(Parent, null, "Parent", ChatId, Settings: parent),
            new ChatBranchView(Child, null, "Child", Parent, Settings: child)
        ]);

    private static ConnectionSettings Connection(string name) =>
        new(Guid.NewGuid(), name, "https://example.test/v1", name.ToLowerInvariant() + "-model", true, false, false);

    private static int Count(string text, string value) => text.Split(value).Length - 1;

    private static async Task<string> RenderAsync(ChatDetails chat, Guid branchId)
    {
        var scheduleApi = new Mock<IChatScheduleApi>();
        scheduleApi.Setup(api => api.GetAsync(ProjectId, ChatId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, Guid branch, CancellationToken _) => new ChatScheduleView(ProjectId, ChatId, "conversation",
                branch == Child && chat.Branches![1].Settings is not null
                    ? new ChatSchedule(new ChatScheduleSettings("Check", new ScheduleRecurrence(ScheduleFrequency.Daily, "2030-01-07", "09:00"), "UTC"), Runs: [])
                    : null,
                "Every weekday at 09:00"));
        var registrations = new ServiceCollection();
        registrations.AddSingleton(scheduleApi.Object);
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = string.Empty;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<BranchSettingsDialog>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(BranchSettingsDialog.ProjectId)] = ProjectId,
                [nameof(BranchSettingsDialog.Chat)] = chat,
                [nameof(BranchSettingsDialog.BranchId)] = branchId,
                [nameof(BranchSettingsDialog.Connections)] = new[] { Fast, Strong },
                [nameof(BranchSettingsDialog.FallbackConnectionId)] = Fast.Id
            }));
            html = component.ToHtmlString();
        });
        return html;
    }
}
