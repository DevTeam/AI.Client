namespace AI.Web.Tests.Components;

using AI.Contracts.Navigation;
using AI.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

public sealed class AppGuideStepRenderingTests
{
    [Theory]
    [InlineData(15, "0:15")]
    [InlineData(59, "0:59")]
    [InlineData(0, "0:00")]
    public async Task ShouldShowTheTimeLimitAndPreventAnExpiredStepFromContinuing(int seconds, string time)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<AppGuideStep>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(AppGuideStep.Step)] = new AppNavigation(Guid.NewGuid(), Target: "project", Action: "show"),
                    [nameof(AppGuideStep.SecondsLeft)] = seconds
                }));
            var html = component.ToHtmlString();
            html.ShouldContain("role=\"timer\" aria-live=\"off\"");
            html.ShouldContain($"Stops automatically in {time}");
            if (seconds == 0) html.ShouldContain("disabled>Continue");
            else html.ShouldNotContain("disabled");
        });
    }

    [Theory]
    [InlineData(15, false, "1")]
    [InlineData(6, false, "0.4")]
    [InlineData(5, true, "0.333")]
    [InlineData(1, true, "0.067")]
    public async Task ShouldWarnDuringTheLastSecondsBeforeTheStepStopsOnItsOwn(int seconds, bool expiring, string remaining)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<AppGuideStep>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(AppGuideStep.Step)] = new AppNavigation(Guid.NewGuid(), Target: "project", Action: "show"),
                    [nameof(AppGuideStep.StepNumber)] = 3,
                    [nameof(AppGuideStep.SecondsLeft)] = seconds,
                    [nameof(AppGuideStep.TotalSeconds)] = 15
                }));
            var html = component.ToHtmlString();
            html.ShouldContain("Step 3");
            html.ShouldContain($"transform: scaleX({remaining})");
            if (expiring) html.ShouldContain("app-guide-step is-expiring");
            else html.ShouldNotContain("is-expiring");
        });
    }

    [Fact]
    public async Task ShouldSayThatAGuideStepContinuesOnItsOwnWhenTheTimeRunsOut()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<AppGuideStep>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(AppGuideStep.Step)] = new AppNavigation(Guid.NewGuid(), Target: "project", Action: "show"),
                    [nameof(AppGuideStep.SecondsLeft)] = 4,
                    [nameof(AppGuideStep.AutoContinue)] = true
                }));
            var html = component.ToHtmlString();
            html.ShouldContain("Continues automatically in 0:04");
            html.ShouldNotContain("Stops automatically");
            html.ShouldContain("app-guide-step is-auto is-expiring");
            html.ShouldContain("Pressed automatically when the time runs out");
        });
    }
}
