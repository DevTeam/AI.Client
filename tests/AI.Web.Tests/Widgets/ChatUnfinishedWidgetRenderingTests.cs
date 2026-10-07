namespace AI.Web.Tests.Widgets;

using System.Net;
using AI.Contracts.Chats;
using AI.Contracts.Navigation;
using AI.Contracts.Runs;
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

public sealed class ChatUnfinishedWidgetRenderingTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private static readonly Guid ProjectId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ChatA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ChatB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task ShouldShowEachUnfinishedRunWithItsReasonAndStatus()
    {
        var stats = new ChatUnfinishedStatistics(
        [
            Task(ChatA, "Alpha", ChatUnfinishedReason.Interrupted, ChatRunStatus.Interrupted),
            Task(ChatB, "Beta", ChatUnfinishedReason.Approval, ChatRunStatus.Generating)
        ], 2, 2);

        var text = WebUtility.HtmlDecode(await RenderAsync(stats));

        text.ShouldContain("Alpha");
        text.ShouldContain("Beta");
        text.ShouldContain("chats");
        text.ShouldContain("interrupted");
        text.ShouldContain("approval");
        text.ShouldContain("Resume all (2)");
    }

    [Fact]
    public async Task ShouldMarkLastActivityTimesAsApproximate()
    {
        var stats = new ChatUnfinishedStatistics(
            [Task(ChatA, "Alpha", ChatUnfinishedReason.Paused, ChatRunStatus.Paused)], 1, 1);

        var text = WebUtility.HtmlDecode(await RenderAsync(stats));

        text.ShouldContain("≈");
        text.ShouldContain("last activity");
    }

    [Fact]
    public async Task ShouldExplainItselfWhenNothingIsUnfinished()
    {
        var text = WebUtility.HtmlDecode(await RenderAsync(ChatUnfinishedStatistics.Empty));

        text.ShouldContain("Unfinished chat work appears here");
    }

    [Fact]
    public async Task ShouldOfferResumeOnlyForTheRunsTheHostWouldAccept()
    {
        var stats = new ChatUnfinishedStatistics(
        [
            Task(ChatA, "Alpha", ChatUnfinishedReason.Running, ChatRunStatus.Generating, canResume: false),
            Task(ChatB, "Beta", ChatUnfinishedReason.Interrupted, ChatRunStatus.Interrupted)
        ], 2, 1);

        var text = WebUtility.HtmlDecode(await RenderAsync(stats));

        text.ShouldContain("running");
        text.ShouldContain("Resume all (1)");
        text.ShouldContain("1 opens its chat");
    }

    private static ChatUnfinishedTask Task(Guid chatId, string title, ChatUnfinishedReason reason,
        ChatRunStatus status, bool canResume = true) =>
        new(chatId, title, chatId, true, status, reason, true, 0, canResume, T0);

    private static async Task<string> RenderAsync(ChatUnfinishedStatistics stats)
    {
        var composition = new Composition("http://127.0.0.1:52173/", publicWeb: true);
        var registrations = new ServiceCollection();
        registrations.AddTransient<IAppNavigationTargets, AppNavigationTargets>();
        registrations.AddTransient<IAppControlHints, AppControlHints>();
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = (ServiceProvider)composition.CreateServiceProvider(composition.CreateBuilder(registrations));
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var definition = new ChatWidgetCatalog(new AppNavigationTargets()).Find(ChatWidgetCatalog.ChatUnfinished)!;
        var widget = new ChatWidgetContext(definition, new(definition.Id), () => System.Threading.Tasks.Task.CompletedTask,
            () => System.Threading.Tasks.Task.CompletedTask, _ => System.Threading.Tasks.Task.CompletedTask);
        var html = string.Empty;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<ChatUnfinishedWidget>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(ChatUnfinishedWidget.Widget)] = widget,
                    [nameof(ChatUnfinishedWidget.Statistics)] = stats
                }));
            html = component.ToHtmlString();
        });
        return html;
    }
}
