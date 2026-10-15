namespace AI.Web.Tests.Components;

using AI.Contracts.Tools;
using AI.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

public sealed class AskUserHistoryRenderingTests
{
    [Fact]
    public async Task AnsweredQuestionsShouldReadAsThePersonsReply()
    {
        var html = await RenderAsync(new AskUserAnswer("answered", [
            new AskUserAnswerItem("Install", "How should the server be installed?", "Portable binary", false),
            new AskUserAnswerItem("Location", "Where should its files go?", "D:\\MCP", true),
            new AskUserAnswerItem("Tests", "Run the tests?", "", false)]));

        html.ShouldContain("workspace-message user user-answer");
        html.ShouldContain("<span class=\"user-prompt-chip\">Install</span>");
        html.ShouldContain("How should the server be installed?");
        html.ShouldContain(">Portable binary</p>");
        html.ShouldContain("user-answer-value is-path\">D:\\MCP</p>");
        html.ShouldContain("user-answer-value is-open\">Left to the assistant</p>");
        html.ShouldNotContain("is-unanswered");
    }

    [Theory]
    [InlineData("dismissed", "You left it to the assistant", "is-unanswered\"")]
    [InlineData("declined", "You declined the question; the assistant stopped", "is-declined")]
    [InlineData("expired", "No answer in time; the assistant decided", "is-expired")]
    public async Task QuestionWithoutAnAnswerShouldSayWhatHappenedInstead(string outcome, string text, string mark)
    {
        var html = await RenderAsync(new AskUserAnswer(outcome, [new AskUserAnswerItem(null, "Which?", "", false)]));

        html.ShouldContain(text);
        html.ShouldContain(mark);
        html.ShouldNotContain("Which?");
    }

    private static async Task<string> RenderAsync(AskUserAnswer answer)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<AskUserAnswerCard>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(AskUserAnswerCard.Answer)] = answer }));
            return component.ToHtmlString();
        });
    }
}
