namespace AI.Web.Tests.Widgets;

using System.Net;
using AI.Contracts.Chats;
using AI.Contracts.Navigation;
using AI.Web.Components;
using AI.Web.Navigation;
using AI.Web.Widgets;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using Shouldly;
using Xunit;

public sealed class ChatTeamWidgetRenderingTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private static readonly Guid BranchA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task ShouldShowEachParticipatingBranchWithItsIntents()
    {
        var html = await RenderAsync(
        [
            User(),
            FromBranch(BranchA, "blocker", T0.AddMinutes(1)),
            FromBranch(BranchA, "status", T0.AddMinutes(2), MessageDelivery.Aside)
        ],
        [new ChatBranchView(BranchA, null, "Researcher", null, null)]);

        var text = WebUtility.HtmlDecode(html);
        text.ShouldContain("Researcher");
        text.ShouldContain("2 messages");
        text.ShouldContain("1 aside");
        text.ShouldContain("1 blocker");
        text.ShouldContain("1 status");
        text.ShouldContain("In 1 of 1 turn team messages arrived");
    }

    [Fact]
    public async Task ShouldMarkArrivalTimesAsApproximate()
    {
        var html = await RenderAsync([User(), FromBranch(BranchA, "status", T0)], null);

        WebUtility.HtmlDecode(html).ShouldContain("≈");
    }

    [Fact]
    public async Task ShouldSayNoBranchWroteAnythingYet()
    {
        var html = await RenderAsync([User(), Assistant()], null);

        WebUtility.HtmlDecode(html)
            .ShouldContain("Branches that send team messages into this one appear here.");
    }

    [Fact]
    public async Task ATeammatesBranchShouldShowTheTaskAndTheWholeTeam()
    {
        var (chat, ada, bo, _, _) = ChatTeamRosterCalculatorTests.Team();

        var html = await RenderAsync([], chat.Branches, chat, ada,
            [ChatTeamRosterCalculatorTests.Run(chat, bo, AI.Contracts.Runs.ChatRunStatus.Failed)]);

        var text = WebUtility.HtmlDecode(html);
        text.ShouldContain("Add CSV export with API, UI and tests");
        text.ShouldContain("Charter");
        text.ShouldContain("1 of 2 done");
        text.ShouldContain("Ada · Backend · this branch");
        text.ShouldContain("Bo · Tests");
        text.ShouldContain("Waiting for the lead: question · Which delimiter?");
        text.ShouldContain("Stopped");
        text.ShouldNotContain("Branches that send team messages into this one appear here.");
    }

    [Fact]
    public async Task TheLeadShouldWearARingLikeTheTeammatesDots()
    {
        var (chat, ada, _, _, _) = ChatTeamRosterCalculatorTests.Team();

        var html = await RenderAsync([], chat.Branches, chat, ada);

        html.ShouldContain("member-dot is-lead");
        html.ShouldNotContain("chat-team-mark\" aria-hidden=\"true\"><svg");
    }

    private static async Task<string> RenderAsync(IReadOnlyList<ChatMessageView> messages,
        IReadOnlyList<ChatBranchView>? branches, ChatDetails? chat = null, Guid? selectedBranchId = null,
        IReadOnlyList<AI.Contracts.Runs.ChatRunSnapshot>? runs = null)
    {
        var composition = new Composition("http://127.0.0.1:52173/", publicWeb: true);
        var registrations = new ServiceCollection();
        registrations.AddTransient<IAppNavigationTargets, AppNavigationTargets>();
        registrations.AddTransient<IAppControlHints, AppControlHints>();
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = (ServiceProvider)composition.CreateServiceProvider(composition.CreateBuilder(registrations));
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var definition = new ChatWidgetCatalog(new AppNavigationTargets()).Find(ChatWidgetCatalog.ChatTeam)!;
        var widget = new ChatWidgetContext(definition, new(definition.Id), () => Task.CompletedTask,
            () => Task.CompletedTask, _ => Task.CompletedTask);
        var html = string.Empty;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<ChatTeamWidget>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(ChatTeamWidget.Widget)] = widget,
                    [nameof(ChatTeamWidget.Messages)] = messages,
                    [nameof(ChatTeamWidget.Branches)] = branches,
                    [nameof(ChatTeamWidget.IsGenerating)] = false,
                    [nameof(ChatTeamWidget.Chat)] = chat,
                    [nameof(ChatTeamWidget.SelectedBranchId)] = selectedBranchId,
                    [nameof(ChatTeamWidget.Runs)] = runs ?? []
                }));
            html = component.ToHtmlString();
        });
        return html;
    }

    private static ChatMessageView User() => new(Guid.NewGuid(), null, "User", "Go", T0);

    private static ChatMessageView Assistant() => new(Guid.NewGuid(), null, "Assistant", "Working", T0);

    private static ChatMessageView FromBranch(Guid branchId, string? intent, DateTimeOffset at,
        MessageDelivery delivery = MessageDelivery.Turn) =>
        new(Guid.NewGuid(), null, "Assistant", "Team message", at,
            Delivery: delivery, Sender: new MessageSender(Guid.NewGuid(), branchId, intent));
}
