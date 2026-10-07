namespace AI.Web.Tests.Components;

using System.Net;
using AI.Contracts.Schedules;
using AI.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

public sealed class BranchPickerMenuRenderingTests
{
    [Fact]
    public async Task ShouldKeepAFewBranchesAPlainList()
    {
        var html = await RenderAsync(
        [
            new BranchPickerItem(1, "Main line", "message-circle", null, null, true, true),
            new BranchPickerItem(2, "Try another approach", "git-branch", null, null, false, false)
        ]);

        html.ShouldNotContain("branch-picker-search");
        html.ShouldNotContain("branch-picker-heading");
        html.ShouldContain("Try another approach");
        html.ShouldContain("ends here");
    }

    [Fact]
    public async Task ShouldGroupManyRunsFilterThemByOutcomeAndShowTheNewestWithTheCurrentOne()
    {
        var statuses = new[] { ScheduleRunStatus.Succeeded, ScheduleRunStatus.Failed, ScheduleRunStatus.Blocked };
        var items = new List<BranchPickerItem> { new(0, "Daily rate", "message-circle", null, null, false, true) };
        for (var number = 1; number <= 20; number++)
            items.Add(new BranchPickerItem(number, $"#{number} · 8 Oct 09:00", "git-branch", null, null,
                Selected: number == 2, EndsHere: false, statuses[number % 3], number, number == 4 ? "manual" : null));

        var text = WebUtility.HtmlDecode(await RenderAsync(items));

        text.ShouldContain("Filter 21 branches");
        text.ShouldContain("Conversation");
        text.ShouldContain("Scheduled runs");
        text.ShouldContain("All <b>20</b>");
        text.ShouldContain("Succeeded <b>6</b>");
        text.ShouldContain("Failed <b>7</b>");
        text.ShouldContain("Waiting <b>7</b>");
        // The newest six, plus the current run although it is older.
        text.ShouldContain("#20 · ");
        text.ShouldContain("#15 · ");
        text.ShouldNotContain("#14 · ");
        text.ShouldContain("#2 · ");
        text.ShouldContain("Show all 20 runs");
        text.ShouldContain("branch-run-dot is-failed");
    }

    [Fact]
    public async Task ShouldShowAndMarkRunsNotSeenYetEvenWhenOlderThanTheNewest()
    {
        var items = new List<BranchPickerItem> { new(0, "Daily rate", "message-circle", null, null, true, true) };
        for (var number = 1; number <= 10; number++)
            items.Add(new BranchPickerItem(number, $"#{number} · 8 Oct 09:00", "git-branch", number == 1 ? "run-status-unread" : null, null,
                Selected: false, EndsHere: false, ScheduleRunStatus.Succeeded, number));

        var text = WebUtility.HtmlDecode(await RenderAsync(items));

        text.ShouldContain("#1 · ");
        text.ShouldNotContain("#2 · ");
        text.ShouldContain("branch-run-dot is-succeeded run-status-unread");
        text.ShouldContain("is-unseen");
    }

    private static async Task<string> RenderAsync(IReadOnlyList<BranchPickerItem> items)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<BranchPickerMenu>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(BranchPickerMenu.Items)] = items }));
            return component.ToHtmlString();
        });
    }
}
