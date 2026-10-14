namespace AI.Web.Tests.Widgets;

using AI.Contracts.Navigation;
using AI.Contracts.Resources;
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

public sealed class ChatWidgetRowRenderingTests
{
    [Fact]
    public async Task ShouldOpenALinkAndOfferToLocateItsUses()
    {
        var html = await RenderAsync(new Dictionary<string, object?>
        {
            [nameof(ChatWidgetRow.Link)] = ChatWidgetActions.SkillLink("review"),
            [nameof(ChatWidgetRow.Uses)] = new[] { Guid.NewGuid(), Guid.NewGuid() }
        });

        html.ShouldContain("chat-widget-row-main is-openable");
        html.ShouldContain("chat-widget-row-away");
        html.ShouldContain("chat-widget-row-locate");
        System.Net.WebUtility.HtmlDecode(html).ShouldContain("Show the latest use in the chat · 2 uses");
    }

    [Fact]
    public async Task ShouldStayInertWithNothingToOpenAndNoUses()
    {
        var html = await RenderAsync(new Dictionary<string, object?>
        {
            [nameof(ChatWidgetRow.Resource)] = new ChatResource(Guid.NewGuid(), ChatResourceKind.Diff, "D:/repo")
        });

        html.ShouldContain("<div class=\"chat-widget-row-main\"");
        html.ShouldNotContain("is-openable");
        html.ShouldNotContain("chat-widget-row-locate");
    }

    [Fact]
    public async Task ShouldOpenInPlaceWithoutTheArrow()
    {
        var html = await RenderAsync(new Dictionary<string, object?>
        {
            [nameof(ChatWidgetRow.OnOpen)] = EventCallback.Factory.Create(new object(), () => { }),
            [nameof(ChatWidgetRow.LeavesChat)] = false
        });

        html.ShouldContain("is-openable");
        html.ShouldNotContain("chat-widget-row-away");
    }

    private static async Task<string> RenderAsync(Dictionary<string, object?> parameters)
    {
        var composition = new Composition("http://127.0.0.1:52173/", publicWeb: true);
        var registrations = new ServiceCollection();
        registrations.AddTransient<IAppNavigationTargets, AppNavigationTargets>();
        registrations.AddTransient<IAppControlHints, AppControlHints>();
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = (ServiceProvider)composition.CreateServiceProvider(composition.CreateBuilder(registrations));
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var definition = new ChatWidgetCatalog(new AppNavigationTargets()).Find(ChatWidgetCatalog.ChatSkills)!;
        parameters[nameof(ChatWidgetRow.Widget)] = new ChatWidgetContext(definition, new(definition.Id),
            () => Task.CompletedTask, () => Task.CompletedTask, _ => Task.CompletedTask);
        parameters[nameof(ChatWidgetRow.Name)] = (RenderFragment)(builder => builder.AddContent(0, "Row"));
        var html = string.Empty;
        await renderer.Dispatcher.InvokeAsync(async () =>
            html = (await renderer.RenderComponentAsync<ChatWidgetRow>(ParameterView.FromDictionary(parameters))).ToHtmlString());
        return html;
    }
}
