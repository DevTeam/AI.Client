namespace AI.Web.Tests.Usage;

using System.Net;
using AI.Contracts.Usage;
using AI.Web.Composer;
using AI.Web.Components;
using AI.Web.Widgets;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using Shouldly;
using Xunit;

public sealed class ChatUsageWidgetRenderingTests
{
    [Theory]
    [InlineData(1_000_000, true)]
    [InlineData(0, false)]
    public async Task ShouldSeparateMeasuredCacheAndEstimatedOverlapIncludingLegacyTotals(long denominator, bool showOverlap)
    {
        var composition = new Composition("http://127.0.0.1:52173/", publicWeb: true);
        var registrations = new ServiceCollection();
        registrations.AddTransient<AI.Contracts.Navigation.IAppNavigationTargets, AI.Contracts.Navigation.AppNavigationTargets>();
        registrations.AddTransient<AI.Web.Navigation.IAppControlHints, AI.Web.Navigation.AppControlHints>();
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = (ServiceProvider)composition.CreateServiceProvider(composition.CreateBuilder(registrations));
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var totals = new TokenUsageTotals(new(627_477, 24_053, 402_432), 23, 0, null, 0, 1_000,
            ReusableInputTokens: 702_260, ToolChanges: 9, PrefixInputTokens: denominator);
        var definition = new ChatWidgetCatalog(new AI.Contracts.Navigation.AppNavigationTargets()).Find(ChatWidgetCatalog.ChatUsage)!;
        var widget = new ChatWidgetContext(definition, new(definition.Id), () => Task.CompletedTask,
            () => Task.CompletedTask, _ => Task.CompletedTask);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<ChatUsageWidget>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(ChatUsageWidget.Widget)] = widget,
                    [nameof(ChatUsageWidget.Context)] = new ComposerContext(250_000, false,
                        [new(ContextLayerKind.Instructions, "Instructions", 19_000, false),
                         new(ContextLayerKind.Tools, "Tools", 6_000, false),
                         new(ContextLayerKind.History, "Conversation", 67_000, false),
                         new(ContextLayerKind.ReservedOutput, "Answer reserve", 10_000, true),
                         new(ContextLayerKind.Overhead, "Overhead", 1_280, true)], true, false, 0),
                    [nameof(ChatUsageWidget.ChatUsage)] = new ChatTokenUsage(Guid.NewGuid(), Guid.NewGuid(), totals, [], [])
                }));
            var html = WebUtility.HtmlDecode(component.ToHtmlString());
            html.ShouldContain("627k in → 24k out");
            html.ShouldContain("≈103k / 250k");
            html.ShouldContain("Cached");
            html.ShouldContain("64%");
            html.ShouldContain("Prefix changes");
            html.ShouldContain("tools 9");
            html.ShouldNotContain("Cache reset");
            html.ShouldNotContain("/100%");
            html.ShouldNotContain("expired or been dropped");
            if (showOverlap)
            {
                html.ShouldContain("Prefix overlap");
                html.ShouldContain("≈70%");
            }
            else html.ShouldNotContain("Prefix overlap");
        });
    }
}
