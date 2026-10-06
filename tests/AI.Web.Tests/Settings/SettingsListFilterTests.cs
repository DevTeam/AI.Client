namespace AI.Web.Tests.Settings;

using System.Text.Json;
using AI.Contracts.Skills;
using AI.Web.Components;
using AI.Web.Notifications;
using AI.Web.Settings;
using AI.Web.Skills;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using Shouldly;
using Xunit;

public sealed class SettingsListFilterTests
{
    private readonly SettingsListFilter _filter = new();

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("push", true)]
    [InlineData("PUSH", true)]
    [InlineData("git push", true)]
    [InlineData("push git", true)]
    [InlineData("git pull", false)]
    [InlineData("pus  gi", true)]
    public void ShouldMatchEveryWordInAnyField(string query, bool expected) =>
        _filter.Matches(query, "Push", null, "Sends commits to a git remote").ShouldBe(expected);

    [Fact]
    public void ShouldMarkEveryOccurrenceOfEveryWord()
    {
        var parts = _filter.Highlight("Read file, write File", "file read");

        parts.ShouldBe([
            new("Read", true), new(" ", false), new("file", true), new(", write ", false), new("File", true)
        ]);
    }

    [Fact]
    public void ShouldMergeOverlappingWords()
    {
        _filter.Highlight("abcd", "abc bcd").ShouldBe([new("abcd", true)]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("zzz")]
    public void ShouldKeepTextWhole(string query)
    {
        _filter.Highlight("Memory", query).ShouldBe([new("Memory", false)]);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task ShouldShowFilterForAnyNonEmptyList(int count, bool shown)
    {
        var schema = JsonSerializer.SerializeToElement(new { type = "object" });
        var skills = Enumerable.Range(1, count)
            .Select(index => new SkillDefinition($"skill-{index}", $"Skill {index}", "Does a thing", "User",
                $"---\nid: skill-{index}\nname: Skill {index}\n---\nText", true, schema))
            .ToArray();
        var api = new Mock<ISkillApi>();
        api.Setup(item => item.ListAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(skills);
        api.Setup(item => item.ListRunsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        await using var services = new ServiceCollection()
            .AddSingleton(api.Object).AddSingleton(Mock.Of<INotificationService>())
            .AddSingleton<ISettingsListFilter, SettingsListFilter>()
            .AddSingleton(Mock.Of<IJSRuntime>()).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<SkillEditor>(ParameterView.Empty)).ToHtmlString());

        html.Contains("settings-filter-input").ShouldBe(shown);
    }
}
