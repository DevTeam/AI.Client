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

public sealed class ChatModelsWidgetRenderingTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task ShouldShowEachModelWithItsShareOfTheAnswers()
    {
        var html = await RenderAsync(ChatUsage(
            Turn([Answer("gpt-5", T0), Answer("gpt-5", T0), Answer("claude", T0)])), liveTurn: null);

        var text = WebUtility.HtmlDecode(html);
        text.ShouldContain("gpt-5");
        text.ShouldContain("claude");
        text.ShouldContain("67%");
        text.ShouldContain("33%");
        text.ShouldContain("In 1 of 1 turn models answered");
    }

    [Fact]
    public async Task ShouldStateThatTokensAreNotRecordedPerModel()
    {
        var html = await RenderAsync(ChatUsage(Turn([Answer("m", T0)])), liveTurn: null);

        WebUtility.HtmlDecode(html).ShouldContain("tokens are not recorded per model");
    }

    [Fact]
    public async Task ShouldSayNoAnswerWasRecordedYet()
    {
        var html = await RenderAsync(chatUsage: null, liveTurn: null);

        WebUtility.HtmlDecode(html)
            .ShouldContain("Models that answer the chat appear here once it sends its first request.");
    }

    private static async Task<string> RenderAsync(ChatTokenUsage? chatUsage, TurnTokenUsage? liveTurn)
    {
        var composition = new Composition("http://127.0.0.1:52173/", publicWeb: true);
        var registrations = new ServiceCollection();
        registrations.AddTransient<IAppNavigationTargets, AppNavigationTargets>();
        registrations.AddTransient<IAppControlHints, AppControlHints>();
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = (ServiceProvider)composition.CreateServiceProvider(composition.CreateBuilder(registrations));
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var definition = new ChatWidgetCatalog(new AppNavigationTargets()).Find(ChatWidgetCatalog.ChatModels)!;
        var widget = new ChatWidgetContext(definition, new(definition.Id), () => Task.CompletedTask,
            () => Task.CompletedTask, _ => Task.CompletedTask);
        var html = string.Empty;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<ChatModelsWidget>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(ChatModelsWidget.Widget)] = widget,
                    [nameof(ChatModelsWidget.ChatUsage)] = chatUsage,
                    [nameof(ChatModelsWidget.LiveTurn)] = liveTurn,
                    [nameof(ChatModelsWidget.Messages)] = new List<ChatMessageView> { User(), Assistant() },
                    [nameof(ChatModelsWidget.IsGenerating)] = false
                }));
            html = component.ToHtmlString();
        });
        return html;
    }

    private static ChatMessageView User() => new(Guid.NewGuid(), null, "User", "Hi", T0);

    private static ChatMessageView Assistant() => new(Guid.NewGuid(), null, "Assistant", "answer", T0);

    private static AnswerModelUsage Answer(string model, DateTimeOffset at) => new(Guid.NewGuid(), at, model);

    private static TurnTokenUsage Turn(IReadOnlyList<AnswerModelUsage> answers) =>
        new(Guid.NewGuid(), Guid.NewGuid(), T0, Totals(answers.Count), [], answers);

    private static ChatTokenUsage ChatUsage(params TurnTokenUsage[] turns) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Totals(turns.Sum(turn => turn.Totals.Requests)), [], turns);

    private static TokenUsageTotals Totals(int requests) => new(new TokenCounts(100, 50), requests, 0, null, 0, 1_000);
}
