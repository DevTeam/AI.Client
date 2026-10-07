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

        text.ShouldContain("Nothing is unfinished");
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
        text.ShouldContain("1 is handled in its chat");
    }

    [Fact]
    public async Task ShouldSayWhatEachRunWaitsOn()
    {
        var stats = new ChatUnfinishedStatistics(
        [
            Task(ChatA, "Alpha", ChatUnfinishedReason.Approval, ChatRunStatus.Generating, canResume: false) with { Detail = "write_file" },
            Task(ChatB, "Beta", ChatUnfinishedReason.Prompt, ChatRunStatus.Generating, canResume: false) with { Detail = "Which folder?" }
        ], 2, 0);

        var text = WebUtility.HtmlDecode(await RenderAsync(stats));

        text.ShouldContain("Approve write_file");
        text.ShouldContain("Asks: Which folder?");
        text.ShouldContain("2 need you");
        text.ShouldNotContain("Resume all");
    }

    [Fact]
    public async Task ShouldNotRepeatAStatusTheReasonAlreadyNames()
    {
        var stats = new ChatUnfinishedStatistics(
            [Task(ChatA, "Alpha", ChatUnfinishedReason.Interrupted, ChatRunStatus.Interrupted)], 1, 1);

        var html = await RenderAsync(stats);

        html.ShouldNotContain("chat-unfinished-status");
    }

    [Fact]
    public async Task ShouldMarkTheRunsOfTheOpenChat()
    {
        var stats = new ChatUnfinishedStatistics(
        [
            Task(ChatA, "Alpha", ChatUnfinishedReason.Paused, ChatRunStatus.Paused),
            Task(ChatB, "Beta", ChatUnfinishedReason.Paused, ChatRunStatus.Paused)
        ], 2, 2);

        var html = WebUtility.HtmlDecode(await RenderAsync(stats, currentChatId: ChatA));

        html.ShouldContain("open now");
        html.ShouldContain("aria-current=\"true\"");
    }

    [Fact]
    public async Task ShouldFoldALongListBehindShowMore()
    {
        var tasks = Enumerable.Range(0, 8)
            .Select(index => Task(Guid.NewGuid(), $"Chat {index}", ChatUnfinishedReason.Interrupted, ChatRunStatus.Interrupted))
            .ToArray();

        var text = WebUtility.HtmlDecode(await RenderAsync(new ChatUnfinishedStatistics(tasks, 8, 8)));

        text.ShouldContain("Chat 5");
        text.ShouldNotContain("Chat 6");
        text.ShouldContain("Show 2 more");
        text.ShouldContain("Resume all (8)");
    }

    [Fact]
    public async Task ShouldOfferTheLegendAsAFilterWhenThereIsMoreThanOneReason()
    {
        var stats = new ChatUnfinishedStatistics(
        [
            Task(ChatA, "Alpha", ChatUnfinishedReason.Interrupted, ChatRunStatus.Interrupted),
            Task(ChatB, "Beta", ChatUnfinishedReason.Running, ChatRunStatus.Generating, canResume: false)
        ], 2, 1);

        var html = await RenderAsync(stats);

        html.ShouldContain("chat-unfinished-filter");
        html.ShouldContain("aria-pressed=\"false\"");
    }

    private static ChatUnfinishedTask Task(Guid chatId, string title, ChatUnfinishedReason reason,
        ChatRunStatus status, bool canResume = true) =>
        new(chatId, title, chatId, true, status, reason, true, 0, canResume, T0);

    private static async Task<string> RenderAsync(ChatUnfinishedStatistics stats, Guid? currentChatId = null)
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
                    [nameof(ChatUnfinishedWidget.Statistics)] = stats,
                    [nameof(ChatUnfinishedWidget.CurrentChatId)] = currentChatId
                }));
            html = component.ToHtmlString();
        });
        return html;
    }
}
