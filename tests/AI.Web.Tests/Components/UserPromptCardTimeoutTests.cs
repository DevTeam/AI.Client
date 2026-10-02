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

public sealed class UserPromptCardTimeoutTests
{
    private static readonly UserPromptQuestion Question = new("start", "Start?", null,
        [new UserPromptOption("Yes", null, true), new UserPromptOption("No", null)], false, false);

    [Theory]
    [InlineData(20, false, "0:20")]
    [InlineData(3, true, "0:03")]
    public async Task ShouldCountDownAnOverlayQuestionLikeAGuideStep(int seconds, bool expiring, string time)
    {
        var html = await RenderAsync(new UserPrompt(Guid.NewGuid(), [Question], 30, "overlay",
            ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(seconds - .5)));

        html.ShouldContain("role=\"timer\" aria-live=\"off\"");
        html.ShouldContain($"Closes automatically in {time}");
        html.ShouldContain("class=\"user-prompt-progress\"");
        html.ShouldContain($"transform: scaleX({(seconds / 30d).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)})");
        html.ShouldNotContain("is-auto");
        if (expiring) html.ShouldContain("user-prompt is-timed is-expiring");
        else html.ShouldNotContain("is-expiring");
    }

    [Fact]
    public async Task ShouldWarnInTheAccentBeforeTheRecommendedAnswersAreSent()
    {
        var html = await RenderAsync(new UserPrompt(Guid.NewGuid(), [Question], 300, SubmitDefaults: true,
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5), DefaultSubmitAt: DateTimeOffset.UtcNow.AddSeconds(3.5)));

        html.ShouldContain("Continues with recommended answers in 0:04");
        html.ShouldContain("user-prompt is-timed is-auto is-expiring");
        html.ShouldContain("Pressed automatically when the time runs out");
    }

    [Fact]
    public async Task ShouldShowNoTimerForAnOverlayThatWaitsForALearningChoice()
    {
        var html = await RenderAsync(new UserPrompt(Guid.NewGuid(), [Question], 0, "overlay"));

        html.ShouldNotContain("role=\"timer\"");
        html.ShouldNotContain("user-prompt-progress");
        html.ShouldContain("class=\"user-prompt\"");
    }

    [Fact]
    public async Task ShouldKeepAnUntimedOverlayQuestionOpenWithoutATimer()
    {
        var answered = false;
        var html = await RenderAsync(new UserPrompt(Guid.NewGuid(), [Question], 0, "overlay"), () => answered = true);

        html.ShouldNotContain("role=\"timer\"");
        html.ShouldNotContain("expires automatically");
        answered.ShouldBeFalse();
    }

    [Fact]
    public async Task ShouldShowNoTimerForAQuestionThatWaits()
    {
        var html = await RenderAsync(new UserPrompt(Guid.NewGuid(), [Question], 300));

        html.ShouldNotContain("role=\"timer\"");
        html.ShouldNotContain("user-prompt-progress");
        html.ShouldContain("class=\"user-prompt\"");
    }

    private static async Task<string> RenderAsync(UserPrompt prompt, Action? answered = null)
    {
        await using var services = new ServiceCollection()
            .AddSingleton<IMarkdownRenderer, SafeMarkdownRenderer>()
            .AddSingleton<IJSRuntime, NoJsRuntime>()
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<UserPromptCard>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(UserPromptCard.Prompt)] = prompt,
                    [nameof(UserPromptCard.OnAnswer)] = (Func<UserPromptResponse, Task<bool>>)(_ => { answered?.Invoke(); return Task.FromResult(true); })
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
