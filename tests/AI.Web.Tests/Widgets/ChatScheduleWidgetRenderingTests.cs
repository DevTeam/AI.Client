namespace AI.Web.Tests.Widgets;

using System.Net;
using System.Text.Json;
using AI.Contracts.Chats;
using AI.Contracts.Navigation;
using AI.Contracts.Runs;
using AI.Contracts.Schedules;
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

public sealed class ChatScheduleWidgetRenderingTests
{
    private static readonly Guid ChatId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid RunBranch = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task ShouldOfferToScheduleAnOrdinaryChat()
    {
        var text = WebUtility.HtmlDecode(await RenderWidgetAsync(Chat("conversation", null)));

        text.ShouldContain("This chat runs only when you write in it");
        text.ShouldContain("Schedule this chat");
    }

    [Fact]
    public async Task ShouldLeadWithTheCountdownAndListTheRunsOfAScheduledChat()
    {
        var now = DateTimeOffset.UtcNow;
        var schedule = new ChatSchedule(Settings(), NextRunAt: now.AddHours(2).AddMinutes(14), RunNumber: 2, Runs:
        [
            new ScheduleRunRecord(Guid.NewGuid(), Guid.NewGuid(), 1, now.AddDays(-1), now.AddDays(-1), 1, ScheduleRunStatus.Succeeded,
                FinishedAt: now.AddDays(-1), Summary: "Build is green", BranchDeleted: true),
            new ScheduleRunRecord(Guid.NewGuid(), RunBranch, 2, now.AddHours(-1), now.AddHours(-1), 1, ScheduleRunStatus.Failed,
                FinishedAt: now.AddHours(-1), Summary: "Two tests failed")
        ]);

        var text = WebUtility.HtmlDecode(await RenderWidgetAsync(Chat(ChatSchedule.Kind, schedule)));

        text.ShouldContain("2h 1");
        text.ShouldContain("until the next run");
        text.ShouldContain("Active");
        text.ShouldContain("Every weekday at 09:00");
        text.ShouldContain("Check the nightly build");
        text.ShouldContain("Up to 2 retries, 15 min apart");
        text.ShouldContain("Delete at once");
        text.ShouldContain("Two tests failed");
        text.ShouldContain("1 of 2 succeeded");
        text.ShouldContain("Run now");
        text.ShouldContain("Pause");
        text.ShouldContain("Remove the schedule");
    }

    [Fact]
    public async Task ShouldSayWhenTheScheduleIsPausedOrWaitsForAPerson()
    {
        var paused = WebUtility.HtmlDecode(await RenderWidgetAsync(Chat(ChatSchedule.Kind,
            new ChatSchedule(Settings(), Paused: true, NextRunAt: DateTimeOffset.UtcNow.AddHours(1), Runs: []))));
        paused.ShouldContain("Paused");
        paused.ShouldContain("Resume");

        var blocked = WebUtility.HtmlDecode(await RenderWidgetAsync(Chat(ChatSchedule.Kind,
            new ChatSchedule(Settings(), NextRunAt: DateTimeOffset.UtcNow.AddHours(1), Runs:
            [
                new ScheduleRunRecord(Guid.NewGuid(), RunBranch, 3, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, ScheduleRunStatus.Blocked)
            ]))));
        blocked.ShouldContain("Waits for you");
        blocked.ShouldContain("Run #3 waits for you: an approval, an answer or a resume.");
        blocked.ShouldContain("Open run");
    }

    [Fact]
    public async Task ShouldRenderTheRecurrencePickerWithAPresetInsideAQuestion()
    {
        var weekly = ScheduleCalendar.Serialize(new ScheduleRecurrence(ScheduleFrequency.Weekly,
            DateOnly.FromDateTime(DateTime.Now).AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), "10:30",
            Weekdays: [ScheduleWeekday.Tuesday]));
        var prompt = new UserPrompt(Guid.NewGuid(),
        [
            new UserPromptQuestion("when", "How often?", "Schedule", [new UserPromptOption("Tuesdays at 10:30", null, true, weekly)],
                false, true, PickerKind: "recurrence"),
            new UserPromptQuestion("day", "Which day?", null, [], false, false, PickerKind: "date"),
            new UserPromptQuestion("time", "What time?", null, [], false, false, PickerKind: "time")
        ], 300);

        var text = WebUtility.HtmlDecode(await RenderAsync<UserPromptCard>(new Dictionary<string, object?>
        {
            [nameof(UserPromptCard.Prompt)] = prompt,
            [nameof(UserPromptCard.OnAnswer)] = (Func<UserPromptResponse, Task<bool>>)(_ => Task.FromResult(true))
        }));

        // The recommended preset is chosen, so the editor shows it ready to adjust.
        text.ShouldContain("recurrence-editor");
        text.ShouldContain("Every week on Tue at 10:30");
        text.ShouldContain("date-picker");
        text.ShouldContain("time-picker");
    }

    private static ChatScheduleSettings Settings() => new(
        "Check the nightly build",
        new ScheduleRecurrence(ScheduleFrequency.Daily, "2030-01-07", "09:00",
            Weekdays: [ScheduleWeekday.Monday, ScheduleWeekday.Tuesday, ScheduleWeekday.Wednesday, ScheduleWeekday.Thursday, ScheduleWeekday.Friday]),
        "UTC", "No errors in the log", new ScheduleRetry(2, 15),
        new ScheduleBranchRetention(new ScheduleRetentionRule(ScheduleRetentionAction.Delete),
            new ScheduleRetentionRule(ScheduleRetentionAction.Keep), new ScheduleRetentionRule(ScheduleRetentionAction.Keep)));

    private static ChatDetails Chat(string kind, ChatSchedule? schedule) => new(ChatId, Guid.NewGuid(), "Nightly", DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow, 3, null,
        [new ChatMessageView(Guid.NewGuid(), null, "User", "Check the nightly build", DateTimeOffset.UtcNow)],
        [new ChatBranchView(ChatId, null, "Nightly"), new ChatBranchView(RunBranch, null, "#2 · run", ChatId)],
        Kind: kind, KindState: schedule is null ? null : JsonSerializer.SerializeToElement(schedule, Json));

    private static Task<string> RenderWidgetAsync(ChatDetails chat)
    {
        var definition = new ChatWidgetCatalog(new AppNavigationTargets()).Find(ChatWidgetCatalog.ChatSchedule)!;
        var widget = new ChatWidgetContext(definition, new(definition.Id), () => Task.CompletedTask, () => Task.CompletedTask,
            _ => Task.CompletedTask);
        return RenderAsync<ChatScheduleWidget>(new Dictionary<string, object?>
        {
            [nameof(ChatScheduleWidget.Widget)] = widget,
            [nameof(ChatScheduleWidget.Chat)] = chat
        });
    }

    private static async Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters) where TComponent : IComponent
    {
        var composition = new Composition("http://127.0.0.1:52173/", publicWeb: true);
        var registrations = new ServiceCollection();
        registrations.AddTransient<IAppNavigationTargets, AppNavigationTargets>();
        registrations.AddTransient<IAppControlHints, AppControlHints>();
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = (ServiceProvider)composition.CreateServiceProvider(composition.CreateBuilder(registrations));
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = string.Empty;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            html = component.ToHtmlString();
        });
        return html;
    }
}
