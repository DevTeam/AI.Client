namespace AI.Web.Tests.Components;

using AI.Contracts.Chats;
using AI.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using Shouldly;
using Xunit;

public sealed class BranchPickerRenderingTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Theory]
    [InlineData(false, "Branch 2 of 2")]
    [InlineData(true, "Branch 1 of 2")]
    public async Task ShouldOfferTheBranchThatStopsWhereAnotherGoesOn(bool showStoppingBranch, string expected)
    {
        // The main line ends at its answer; a branch forked from that last answer goes on from it.
        var question = Message("User", null, 0);
        var answer = Message("Assistant", question.Id, 1);
        var followUp = Message("User", answer.Id, 2);
        var followUpAnswer = Message("Assistant", followUp.Id, 3);
        var chatId = Guid.NewGuid();
        var chat = Chat(chatId, [question, answer, followUp, followUpAnswer],
        [
            new ChatBranchView(chatId, answer.Id, "Main"),
            new ChatBranchView(Guid.NewGuid(), followUpAnswer.Id, "Branch", chatId, followUp.Id)
        ]);

        var html = await RenderAsync(chat, showStoppingBranch ? answer.Id : followUpAnswer.Id, answer.Id);

        html.ShouldContain(expected);
        // Both lines are named as the sidebar names them: the main line by its chat.
        Labels(html).ShouldBe(["Branches", "Branch"]);
        html.ShouldContain("ends at this message");
    }

    [Fact]
    public async Task ShouldNameEveryContinuationAfterTheBranchItLeadsInto()
    {
        // main: question → answer → mainFollowUp; "First" forks at answer; "Nested" forks inside "First".
        var question = Message("User", null, 0);
        var answer = Message("Assistant", question.Id, 1);
        var mainFollowUp = Message("User", answer.Id, 2);
        var firstFollowUp = Message("User", answer.Id, 3);
        var firstAnswer = Message("Assistant", firstFollowUp.Id, 4);
        var nestedAnswer = Message("Assistant", firstFollowUp.Id, 5);
        var chatId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var chat = Chat(chatId, [question, answer, mainFollowUp, firstFollowUp, firstAnswer, nestedAnswer],
        [
            new ChatBranchView(chatId, mainFollowUp.Id, "Main"),
            new ChatBranchView(firstId, firstAnswer.Id, "First", chatId, firstFollowUp.Id),
            new ChatBranchView(Guid.NewGuid(), nestedAnswer.Id, "Nested", firstId, nestedAnswer.Id)
        ]);

        var atAnswer = await RenderAsync(chat, mainFollowUp.Id, answer.Id);
        var inFirst = await RenderAsync(chat, firstAnswer.Id, firstFollowUp.Id);

        Labels(atAnswer).ShouldBe(["Branches", "First"]);
        Labels(inFirst).ShouldBe(["First", "Nested"]);
    }

    [Fact]
    public async Task ShouldListTheLinesInTheSidebarOrder()
    {
        // The chat stores "Listed first" before "Listed second", although its message is younger.
        var question = Message("User", null, 0);
        var answer = Message("Assistant", question.Id, 1);
        var mainFollowUp = Message("User", answer.Id, 2);
        var older = Message("User", answer.Id, 3);
        var younger = Message("User", answer.Id, 4);
        var chatId = Guid.NewGuid();
        var chat = Chat(chatId, [question, answer, mainFollowUp, older, younger],
        [
            new ChatBranchView(Guid.NewGuid(), younger.Id, "Listed first", chatId, younger.Id),
            new ChatBranchView(chatId, mainFollowUp.Id, "Main"),
            new ChatBranchView(Guid.NewGuid(), older.Id, "Listed second", chatId, older.Id)
        ]);

        var html = await RenderAsync(chat, mainFollowUp.Id, answer.Id);

        Labels(html).ShouldBe(["Branches", "Listed first", "Listed second"]);
    }

    [Fact]
    public async Task ShouldNotOfferAPickerWithoutASecondLine()
    {
        var question = Message("User", null, 0);
        var answer = Message("Assistant", question.Id, 1);
        var followUp = Message("User", answer.Id, 2);
        var chatId = Guid.NewGuid();
        var chat = Chat(chatId, [question, answer, followUp], [new ChatBranchView(chatId, followUp.Id, "Main")]);

        var html = await RenderAsync(chat, followUp.Id, null);

        html.ShouldNotContain("branch-picker-trigger");
    }

    [Fact]
    public async Task ShouldHangThePickerOfAFoldedStepUnderTheTurnSummary()
    {
        // The branch leaves a progress note that the main line folds into "Worked for …".
        var question = Message("User", null, 0);
        var note = Message("Assistant", question.Id, 1);
        var answer = Message("Assistant", note.Id, 2);
        var otherQuestion = Message("User", note.Id, 3);
        var chatId = Guid.NewGuid();
        var chat = Chat(chatId, [question, note, answer, otherQuestion],
        [
            new ChatBranchView(chatId, answer.Id, "Main"),
            new ChatBranchView(Guid.NewGuid(), otherQuestion.Id, "Branch", chatId, otherQuestion.Id)
        ]);

        var html = await RenderAsync(chat, answer.Id, null);

        var forks = html.IndexOf("class=\"turn-forks\"", StringComparison.Ordinal);
        forks.ShouldBeGreaterThan(html.IndexOf("turn-summary", StringComparison.Ordinal));
        html.IndexOf("Branch 1 of 2", StringComparison.Ordinal).ShouldBeGreaterThan(forks);
        html.ShouldContain("Branches start inside this turn");
    }

    private static string[] Labels(string html) => html
        .Split("<span class=\"branch-option-label\">").Skip(1)
        .Select(fragment => fragment[..fragment.IndexOf("</span>", StringComparison.Ordinal)])
        .ToArray();

    private static ChatMessageView Message(string role, Guid? parentId, int second) =>
        new(Guid.NewGuid(), parentId, role, $"{role} {second}", Now.AddSeconds(second));

    private static ChatDetails Chat(Guid id, IReadOnlyList<ChatMessageView> messages, IReadOnlyList<ChatBranchView> branches) =>
        new(id, Guid.NewGuid(), "Branches", Now, Now, 1, null, messages, branches);

    private static async Task<string> RenderAsync(ChatDetails chat, Guid leafId, Guid? openMenuId)
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
                    [nameof(MessageFeed.MessageBranchMenuId)] = openMenuId
                }));
            return component.ToHtmlString();
        });
    }
}
