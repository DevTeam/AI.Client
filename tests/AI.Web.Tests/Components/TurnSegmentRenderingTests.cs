using AI.Contracts.Chat;
using AI.Contracts.Chats;
using AI.Contracts.Tools;
using AI.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using Shouldly;
using Xunit;

namespace AI.Web.Tests.Components;

public sealed class TurnSegmentRenderingTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task ShouldDrawEachPinBetweenTheFoldedStepsAroundIt()
    {
        var question = Message("User", "Fix the build", 0);
        var firstNote = Message("Assistant", "Reading the project", 1);
        var added = Message("User", "Do not touch migrations", 2) with { Delivery = MessageDelivery.InTurn };
        var ask = Message("Assistant", "", 3) with { ToolCalls = [new ChatToolCall("ask-1", "mcp_app__ask_user", "{}")] };
        var reply = Message("Tool", "{}", 40) with
        {
            ToolCallId = "ask-1",
            UserAnswer = new AskUserAnswer("answered", [new AskUserAnswerItem(null, "Which branch?", "master", false)])
        };
        var lastNote = Message("Assistant", "Building now", 42);
        var answer = Message("Assistant", "Fixed", 45);
        var chat = Chat([question, firstNote, added, ask, reply, lastNote, answer]);

        var html = await RenderAsync(chat, answer.Id);

        var firstRow = html.IndexOf($"id=\"turn-summary-{question.Id}\"", StringComparison.Ordinal);
        var remark = html.IndexOf("Do not touch migrations", StringComparison.Ordinal);
        var secondRow = html.IndexOf($"id=\"turn-summary-{question.Id}-1\"", StringComparison.Ordinal);
        var replyAt = html.IndexOf(">master<", StringComparison.Ordinal);
        var thirdRow = html.IndexOf($"id=\"turn-summary-{question.Id}-2\"", StringComparison.Ordinal);
        var final = html.IndexOf("Fixed", StringComparison.Ordinal);
        firstRow.ShouldBeGreaterThan(-1);
        remark.ShouldBeGreaterThan(firstRow);
        secondRow.ShouldBeGreaterThan(remark);
        replyAt.ShouldBeGreaterThan(secondRow);
        thirdRow.ShouldBeGreaterThan(replyAt);
        final.ShouldBeGreaterThan(thirdRow);
        // Folded: the steps themselves are not drawn, and each row counts only its own time.
        html.ShouldNotContain("Reading the project");
        html.ShouldNotContain("Building now");
        html.Split("Worked for 1s").Length.ShouldBe(3);
        html.ShouldContain("Worked for 5s");
        html.ShouldNotContain("Worked for 45s");
    }

    [Fact]
    public async Task ShouldUnfoldOnlyTheSegmentHoldingTheStepAndKeepItsStepsAbovePin()
    {
        var question = Message("User", "Fix the build", 0);
        var firstNote = Message("Assistant", "Reading the project", 1);
        var added = Message("User", "Do not touch migrations", 2) with { Delivery = MessageDelivery.InTurn };
        var secondNote = Message("Assistant", "Checking the migrations folder", 3);
        var insisted = Message("User", "Leave them alone", 4) with { Delivery = MessageDelivery.InTurn };
        var lastNote = Message("Assistant", "Building now", 5);
        var answer = Message("Assistant", "Fixed", 6);
        var chat = Chat([question, firstNote, added, secondNote, insisted, lastNote, answer]);
        var chained = chat.Messages;

        // A fork started from a step keeps its segment open, the way a click does.
        var html = await RenderAsync(chat, chained[^1].Id, forkSourceId: chained[3].Id);

        var remark = html.IndexOf("Do not touch migrations", StringComparison.Ordinal);
        var row = html.IndexOf($"id=\"turn-summary-{chained[0].Id}-1\"", StringComparison.Ordinal);
        var step = html.IndexOf("Checking the migrations folder", StringComparison.Ordinal);
        var hide = html.IndexOf($"id=\"turn-collapse-{chained[0].Id}-1\"", StringComparison.Ordinal);
        var pin = html.IndexOf("Leave them alone", StringComparison.Ordinal);
        row.ShouldBeGreaterThan(remark);
        step.ShouldBeGreaterThan(row);
        pin.ShouldBeGreaterThan(step);
        hide.ShouldBe(-1);
        html.ShouldNotContain("Reading the project");
        html.ShouldNotContain("Building now");
    }

    [Fact]
    public async Task ShouldKeepOneRowForATurnWithoutPins()
    {
        var question = Message("User", "Fix the build", 0);
        var note = Message("Assistant", "Reading the project", 1);
        var answer = Message("Assistant", "Fixed", 3);

        var html = await RenderAsync(Chat([question, note, answer]), answer.Id);

        html.Split("id=\"turn-summary-").Length.ShouldBe(2);
        html.ShouldContain($"id=\"turn-summary-{question.Id}\"");
        html.ShouldContain("Worked for 3s");
    }

    [Fact]
    public async Task ShouldDrawNoRowForAPinWithoutStepsBeforeOrAfterIt()
    {
        var question = Message("User", "Fix the build", 0);
        var added = Message("User", "Also the docs", 1) with { Delivery = MessageDelivery.InTurn };
        var answer = Message("Assistant", "Fixed", 3);

        var html = await RenderAsync(Chat([question, added, answer]), answer.Id);

        html.ShouldNotContain("id=\"turn-summary-");
        html.IndexOf("Also the docs", StringComparison.Ordinal)
            .ShouldBeLessThan(html.IndexOf("Fixed", StringComparison.Ordinal));
    }

    private static ChatMessageView Message(string role, string content, int second, Guid? parentId = null) =>
        new(Guid.NewGuid(), parentId, role, content, Now.AddSeconds(second));

    // Chains the messages in order, as the branch the feed shows.
    private static ChatDetails Chat(IReadOnlyList<ChatMessageView> messages)
    {
        var chained = new List<ChatMessageView>();
        foreach (var message in messages)
            chained.Add(message with { ParentId = chained.Count == 0 ? null : chained[^1].Id });
        var id = Guid.NewGuid();
        return new ChatDetails(id, Guid.NewGuid(), "Segments", Now, Now, 1, null, chained,
            [new ChatBranchView(id, chained[^1].Id, "Main")]);
    }

    private static async Task<string> RenderAsync(ChatDetails chat, Guid leafId, Guid? forkSourceId = null)
    {
        var composition = new Composition("http://127.0.0.1:52173/", publicWeb: true);
        var registrations = new ServiceCollection();
        registrations.AddTransient<AI.Contracts.Navigation.IAppNavigationTargets, AI.Contracts.Navigation.AppNavigationTargets>();
        registrations.AddTransient<AI.Web.Navigation.IAppControlHints, AI.Web.Navigation.AppControlHints>();
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = (ServiceProvider)composition.CreateServiceProvider(composition.CreateBuilder(registrations));
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<MessageFeed>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(MessageFeed.HasSelectedProject)] = true,
                    [nameof(MessageFeed.SelectedChat)] = chat,
                    [nameof(MessageFeed.BranchLeafId)] = leafId,
                    [nameof(MessageFeed.ForkSourceId)] = forkSourceId
                }));
            return component.ToHtmlString();
        });
    }
}
