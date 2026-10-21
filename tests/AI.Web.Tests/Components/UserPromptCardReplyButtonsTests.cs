namespace AI.Web.Tests.Components;

using AI.Contracts.Runs;
using AI.Web.Components;
using AI.Web.Markdown;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Shouldly;
using Xunit;

public sealed class UserPromptCardReplyButtonsTests
{
    private static readonly UserPromptQuestion Question = new("go", "Apply the fix?", null,
        [new UserPromptOption("Yes", "Patch the file", true), new UserPromptOption("No", null)], false, true);

    [Fact]
    public async Task ShouldDrawEachOptionAsAReplyButtonWithNoAnswerButton()
    {
        var html = await RenderAsync(new UserPrompt(Guid.NewGuid(), [Question], 300, ReplyButtons: true));

        html.ShouldContain("class=\"user-prompt-reply is-recommended\"");
        html.ShouldContain("aria-keyshortcuts=\"2\"");
        html.ShouldContain("Patch the file");
        // The card says that a click sends, and offers nothing else to press for the answer.
        html.ShouldContain("Click an answer to send it right away");
        html.ShouldNotContain("user-prompt-submit");
        html.ShouldNotContain("type=\"radio\"");
        html.ShouldContain("Or type your own answer and press Enter");
        html.ShouldContain("Decide yourself");
    }

    [Fact]
    public async Task ShouldKeepTheFormForAnOrdinaryQuestion()
    {
        var html = await RenderAsync(new UserPrompt(Guid.NewGuid(), [Question], 300));

        html.ShouldContain("user-prompt-submit");
        html.ShouldContain("type=\"radio\"");
        html.ShouldNotContain("user-prompt-reply");
    }

    private static async Task<string> RenderAsync(UserPrompt prompt)
    {
        await using var services = new ServiceCollection()
            .AddSingleton<IMarkdownRenderer, SafeMarkdownRenderer>()
            .AddSingleton<IJSRuntime, NoJsRuntime>()
            .AddSingleton<AI.Contracts.Schedules.IScheduleCalendar, AI.Contracts.Schedules.ScheduleCalendar>()
            .AddSingleton<AI.Contracts.Schedules.IScheduleDescriptions, AI.Contracts.Schedules.ScheduleDescriptions>()
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<UserPromptCard>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(UserPromptCard.Prompt)] = prompt,
                    [nameof(UserPromptCard.OnAnswer)] = (Func<UserPromptResponse, Task<bool>>)(_ => Task.FromResult(true))
                }));
            return component.ToHtmlString();
        });
    }

    private sealed class NoJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromException<TValue>(new JSException("No JavaScript in this test."));

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
