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

public sealed class UserPromptCardInputsTests
{
    [Fact]
    public async Task ShouldPrefillTheSliderWithTheDefaultAndShowTheLivePreview()
    {
        var question = new UserPromptQuestion("timeout", "Request timeout?", null, [new UserPromptOption("Fast", null, false, "500")], false, false,
            Input: new UserPromptInput(UserPromptInputKind.Number, 100, 5000, 100, "ms", [new UserPromptMark(1000, "Default")]),
            Defaults: ["1500"], Preview: "≈ {value / 1000:1} s");

        var html = await RenderAsync(question);

        html.ShouldContain("type=\"range\"");
        html.ShouldContain("value=\"1500\"");
        html.ShouldContain("≈ 1.5 s");
        html.ShouldContain(">Default<");
        html.ShouldContain("class=\"user-prompt-preset\"");
        // The default is a real answer: Continue is offered at once.
        html.ShouldContain("All questions answered");
    }

    [Fact]
    public async Task ShouldLeaveAnUntouchedSliderUnset()
    {
        var question = new UserPromptQuestion("count", "How many?", null, [], false, false,
            Input: new UserPromptInput(UserPromptInputKind.Number, 0, 10));

        var html = await RenderAsync(question);

        html.ShouldContain("user-prompt-slider is-unset");
        html.ShouldContain("aria-valuetext=\"Not set\"");
        html.ShouldNotContain("All questions answered");
    }

    [Fact]
    public async Task ShouldDrawARankInTheOrderOfferedWithWaysToMoveEachRow()
    {
        var question = new UserPromptQuestion("order", "What first?", null,
            [new UserPromptOption("Tests", null), new UserPromptOption("Feature", null)], false, false,
            Input: new UserPromptInput(UserPromptInputKind.Rank));

        var html = await RenderAsync(question);

        html.ShouldContain("aria-label=\"1. Tests\"");
        html.ShouldContain("aria-label=\"2. Feature\"");
        html.ShouldContain("Move Tests down");
        html.ShouldContain("draggable=\"true\"");
    }

    [Fact]
    public async Task ShouldShowDigitKeysOnTheOptionsAndOfferToRememberALastingAnswer()
    {
        var question = new UserPromptQuestion("tests", "Where do tests go?", null,
            [new UserPromptOption("Next to the code", null), new UserPromptOption("In tests/", null)], false, true, Rememberable: true);

        var html = await RenderAsync(question);

        html.ShouldContain("class=\"user-prompt-option-key\"");
        html.ShouldContain("to pick in the question you are on");
        html.ShouldContain("Remember my answer");
    }

    [Fact]
    public async Task ShouldDrawScaleStepsAndSwatches()
    {
        var scale = new UserPromptQuestion("weight", "How important?", null, [], false, false,
            Input: new UserPromptInput(UserPromptInputKind.Scale, Marks: [new UserPromptMark(1, "Low"), new UserPromptMark(5, "Critical")]));
        var colour = new UserPromptQuestion("accent", "Accent?", null, [new UserPromptOption("Blue", null, true, "#3b82f6")], false, false,
            Input: new UserPromptInput(UserPromptInputKind.Color));

        var html = await RenderAsync(scale, colour);

        html.ShouldContain("role=\"radiogroup\"");
        html.ShouldContain("aria-label=\"5, Critical\"");
        html.ShouldContain("--swatch: #3b82f6");
        // The recommended swatch is the colour the card starts with.
        html.ShouldContain("value=\"#3b82f6\"");
    }

    [Fact]
    public async Task ShouldDrawCodeAsMonospaceTextWithItsLanguage()
    {
        var question = new UserPromptQuestion("regex", "Pattern?", null, [], false, true,
            Input: new UserPromptInput(UserPromptInputKind.Text, Multiline: true, Language: "regex"), Defaults: ["^v\\d+$"]);

        var html = await RenderAsync(question);

        html.ShouldContain("user-prompt-text is-code");
        html.ShouldContain("<textarea");
        html.ShouldContain(">regex<");
    }

    private static async Task<string> RenderAsync(params UserPromptQuestion[] questions)
    {
        await using var services = new ServiceCollection()
            .AddSingleton<IMarkdownRenderer, SafeMarkdownRenderer>()
            .AddSingleton<IJSRuntime, NoJsRuntime>()
            .AddSingleton<AI.Contracts.Schedules.IScheduleCalendar, AI.Contracts.Schedules.ScheduleCalendar>()
            .AddSingleton<AI.Contracts.Schedules.IScheduleDescriptions, AI.Contracts.Schedules.ScheduleDescriptions>()
            .AddSingleton<IUserPromptInputs, UserPromptInputs>()
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<UserPromptCard>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(UserPromptCard.Prompt)] = new UserPrompt(Guid.NewGuid(), questions, 900),
                    [nameof(UserPromptCard.OnAnswer)] = (Func<UserPromptResponse, Task<bool>>)(_ => Task.FromResult(true))
                }));
            return System.Net.WebUtility.HtmlDecode(component.ToHtmlString());
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
