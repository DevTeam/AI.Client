namespace AI.Web.Tests.Widgets;

using System.Net;
using AI.Contracts.Chats;
using AI.Contracts.Navigation;
using AI.Contracts.Usage;
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

public sealed class ChatTimelineWidgetRenderingTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task ShouldShowTheTimeBreakdownAboveTheTurnList()
    {
        var html = await RenderAsync(
            [User(T0), Assistant(T0.AddSeconds(5)), User(T0.AddSeconds(30)), Assistant(T0.AddSeconds(50))],
            chatUsage: null);

        var text = WebUtility.HtmlDecode(html);
        text.ShouldContain("chat-timeline-time-ring");
        text.ShouldContain("Model requests");
        text.ShouldContain("Inside turns");
        text.ShouldContain("Between turns");
        // Two 5 s turns and the 25 s gap between them, each with its duration in the legend.
        text.ShouldContain("25 s");
        text.ShouldContain("50%");
        text.ShouldContain("Where the branch's");
        // The ring comes before the list, so it reads as the summary of the turns below it.
        text.IndexOf("chat-timeline-time-ring", StringComparison.Ordinal)
            .ShouldBeLessThan(text.IndexOf("chat-timeline-list", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShouldShowProviderTimeAsItsOwnPart()
    {
        var turn = new TurnTokenUsage(Guid.NewGuid(), Guid.NewGuid(), T0,
            new TokenUsageTotals(new TokenCounts(10, 5), 1, 0, null, 0, 3_000), []);

        var html = await RenderAsync(
            [User(T0), Assistant(T0.AddSeconds(5))],
            new ChatTokenUsage(Guid.NewGuid(), Guid.NewGuid(),
                new TokenUsageTotals(new TokenCounts(10, 5), 1, 0, null, 0, 3_000), [], [turn]));

        var text = WebUtility.HtmlDecode(html);
        text.ShouldContain("chat-timeline-time-model");
        text.ShouldContain("3 s");
        text.ShouldContain("2 s");
    }

    [Fact]
    public async Task ShouldNotDrawTheRingWhenNothingCanBeMeasured()
    {
        var html = await RenderAsync([User(T0)], chatUsage: null);

        var text = WebUtility.HtmlDecode(html);
        text.ShouldNotContain("chat-timeline-time-ring");
        text.ShouldContain("1");
        text.ShouldContain("turn");
    }

    private static async Task<string> RenderAsync(
        IReadOnlyList<ChatMessageView> messages, ChatTokenUsage? chatUsage)
    {
        var composition = new Composition("http://127.0.0.1:52173/", publicWeb: true);
        var registrations = new ServiceCollection();
        registrations.AddTransient<IAppNavigationTargets, AppNavigationTargets>();
        registrations.AddTransient<IAppControlHints, AppControlHints>();
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = (ServiceProvider)composition.CreateServiceProvider(composition.CreateBuilder(registrations));
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var definition = new ChatWidgetCatalog(new AppNavigationTargets()).Find(ChatWidgetCatalog.ChatTimeline)!;
        var widget = new ChatWidgetContext(definition, new(definition.Id), () => Task.CompletedTask,
            () => Task.CompletedTask, _ => Task.CompletedTask);
        var html = string.Empty;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<ChatTimelineWidget>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(ChatTimelineWidget.Widget)] = widget,
                    [nameof(ChatTimelineWidget.Messages)] = messages,
                    [nameof(ChatTimelineWidget.ChatUsage)] = chatUsage,
                    [nameof(ChatTimelineWidget.IsGenerating)] = false
                }));
            html = component.ToHtmlString();
        });
        return html;
    }

    private static ChatMessageView User(DateTimeOffset at) => new(Guid.NewGuid(), null, "User", "Hi", at);

    private static ChatMessageView Assistant(DateTimeOffset at) => new(Guid.NewGuid(), null, "Assistant", "answer", at);
}
