namespace AI.Web.Tests.Widgets;

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

public sealed class AppGuideWidgetRenderingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ShouldRenderTheGuideInsideTheSharedRailWithoutBreakingOtherWidgets(bool collapsed, bool hidden)
    {
        var composition = new Composition("http://127.0.0.1:52173/", publicWeb: true);
        var registrations = new ServiceCollection();
        registrations.AddTransient<AI.Contracts.Navigation.IAppNavigationTargets, AI.Contracts.Navigation.AppNavigationTargets>();
        registrations.AddTransient<AI.Web.Navigation.IAppControlHints, AI.Web.Navigation.AppControlHints>();
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = (ServiceProvider)composition.CreateServiceProvider(composition.CreateBuilder(registrations));
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            RenderFragment<ChatWidgetContext> template = widget => builder =>
            {
                if (widget.Definition.Id == ChatWidgetCatalog.AppGuide)
                {
                    builder.OpenComponent<AppGuideWidget>(0);
                    builder.AddAttribute(1, nameof(AppGuideWidget.Widget), widget);
                    builder.AddAttribute(2, nameof(AppGuideWidget.Running), true);
                    builder.AddAttribute(3, nameof(AppGuideWidget.StepNumber), 2);
                }
                else
                {
                    builder.OpenComponent<ChatToolsWidget>(4);
                    builder.AddAttribute(5, nameof(ChatToolsWidget.Widget), widget);
                }
                builder.CloseComponent();
            };
            var component = await renderer.RenderComponentAsync<ChatWidgetRail>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(ChatWidgetRail.Widgets)] = new ChatWidgetPreference[]
                    {
                        new(ChatWidgetCatalog.ChatTools),
                        new(ChatWidgetCatalog.AppGuide, Hidden: hidden, Collapsed: collapsed)
                    },
                    [nameof(ChatWidgetRail.WidgetTemplate)] = template
                }));
            var html = component.ToHtmlString();
            html.ShouldContain("data-app-target=\"widgets\"");
            html.ShouldContain("data-app-target=\"widgets.menu\"");
            html.ShouldContain("data-app-target=\"widgets.chat-tools\"");
            html.ShouldContain("data-widget-id=\"chat-tools\"");
            html.ShouldContain("Tool calls appear here");
            html.ShouldContain("Close widgets");
            html.ShouldNotContain("app-guide-widget");
            if (hidden)
            {
                html.ShouldNotContain("data-widget-id=\"app-guide\"");
            }
            else
            {
                html.ShouldContain("data-widget-id=\"app-guide\"");
                html.ShouldContain("data-app-target=\"widgets.app-guide\"");
                html.ShouldContain("Move Application guide");
                html.ShouldContain("Hide Application guide");
                if (collapsed)
                {
                    html.ShouldContain("Step 2");
                    html.ShouldNotContain("app-guide-topics");
                }
                else
                {
                    html.ShouldContain("app-guide-topics");
                    html.ShouldContain("Pause");
                    html.ShouldContain("Stop");
                }
            }
        });
    }

    [Fact]
    public async Task ShouldTemporarilyRevealACollapsedWidgetWithoutSavingOrShowingHiddenWidgets()
    {
        var composition = new Composition("http://127.0.0.1:52173/", publicWeb: true);
        var registrations = new ServiceCollection();
        registrations.AddTransient<AI.Contracts.Navigation.IAppNavigationTargets, AI.Contracts.Navigation.AppNavigationTargets>();
        registrations.AddTransient<AI.Web.Navigation.IAppControlHints, AI.Web.Navigation.AppControlHints>();
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = (ServiceProvider)composition.CreateServiceProvider(composition.CreateBuilder(registrations));
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var saved = new ChatWidgetPreference[] { new(ChatWidgetCatalog.ChatTools, Collapsed: true),
            new(ChatWidgetCatalog.ChatUsage, Hidden: true, Collapsed: true) };
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            RenderFragment<ChatWidgetContext> template = widget => builder =>
            {
                builder.OpenComponent<ChatToolsWidget>(0);
                builder.AddAttribute(1, nameof(ChatToolsWidget.Widget), widget);
                builder.CloseComponent();
            };
            async Task<string> RenderAsync(string? expanded)
            {
                var component = await renderer.RenderComponentAsync<ChatWidgetRail>(ParameterView.FromDictionary(
                    new Dictionary<string, object?> { [nameof(ChatWidgetRail.Widgets)] = saved,
                        [nameof(ChatWidgetRail.WidgetTemplate)] = template,
                        [nameof(ChatWidgetRail.GuideExpandedWidgetId)] = expanded }));
                return component.ToHtmlString();
            }
            (await RenderAsync(null)).ShouldNotContain("Tool calls appear here");
            var showing = await RenderAsync(ChatWidgetCatalog.ChatTools);
            showing.ShouldContain("Tool calls appear here");
            showing.ShouldNotContain("data-widget-id=\"chat-usage\"");
            saved[0].Collapsed.ShouldBeTrue();
            var hiddenShowing = await RenderAsync(ChatWidgetCatalog.ChatUsage);
            hiddenShowing.ShouldContain("data-widget-id=\"chat-usage\"");
            saved[1].Hidden.ShouldBeTrue();
            saved[1].Collapsed.ShouldBeTrue();
            (await RenderAsync(null)).ShouldNotContain("Tool calls appear here");
        });
    }

    [Fact]
    public void ShouldKeepTheSavedWidgetOrderAndStatesWhenAddingTheGuide()
    {
        var catalog = new ChatWidgetCatalog(new AI.Contracts.Navigation.AppNavigationTargets());
        var layout = new ChatWidgetLayout(catalog);
        var saved = catalog.Widgets.Where(widget => widget.Id != ChatWidgetCatalog.AppGuide).Reverse()
            .Select(widget => new ChatWidgetPreference(widget.Id, Hidden: true, Collapsed: true)).ToArray();
        var arranged = layout.Arrange(saved);
        arranged.Take(saved.Length).ShouldBe(saved);
        arranged[^1].ShouldBe(new ChatWidgetPreference(ChatWidgetCatalog.AppGuide));
        layout.Update(arranged, ChatWidgetCatalog.AppGuide, widget => widget with { Hidden = true })
            .Take(saved.Length).ShouldBe(saved);
    }
}
