namespace AI.Infrastructure.Tests.Tools;

using AI.Contracts.Runs;
using Shouldly;
using System.Text.Json;
using Xunit;

/// <summary>The schedule pickers of ask_user and the app_schedule tool, through the in-process MCP transport.</summary>
public sealed partial class AppToolTests
{
    private const string WeekdaysAtNine =
        "{\"frequency\":\"Daily\",\"start\":\"2030-01-07\",\"time\":\"09:00\",\"weekdays\":[\"Monday\",\"Tuesday\",\"Wednesday\",\"Thursday\",\"Friday\"]}";

    [Fact]
    public async Task AskUserShouldReturnExactDatesAndDropValuesThePickerCannotReturn()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        fixture.Broker.Answer = _ => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered,
            [new UserPromptAnswer("dates", [], null, Values: [" 2030-01-08 ", "next friday", "2030-01-09", "2030-01-08"])]);

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[] { new { id = "dates", text = "Which days?", options = Array.Empty<object>(), pickerKind = "date", multiSelect = true, allowOther = false } }
        });

        result.GetProperty("answers")[0].GetProperty("values").EnumerateArray().Select(item => item.GetString())
            .ShouldBe(["2030-01-08", "2030-01-09"]);
        fixture.Broker.LastRequest!.Questions[0].PickerKind.ShouldBe("date");
        fixture.Broker.LastRequest.Questions[0].RepositoryPath.ShouldBeNull();
    }

    [Fact]
    public async Task AskUserShouldReturnAChosenPresetValueAndDescribeRecurrences()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        fixture.Broker.Answer = _ => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered,
            [new UserPromptAnswer("when", [0], null)]);

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[]
            {
                new
                {
                    id = "when", text = "How often?", pickerKind = "recurrence", allowOther = true,
                    options = new[] { new { label = "Weekdays at 9:00", value = (string?)WeekdaysAtNine }, new { label = "Mondays", value = (string?)null } }
                }
            }
        });

        var answer = result.GetProperty("answers")[0];
        answer.GetProperty("selected")[0].GetString().ShouldBe("Weekdays at 9:00");
        var value = answer.GetProperty("values")[0].GetString()!;
        JsonDocument.Parse(value).RootElement.GetProperty("frequency").GetString().ShouldBe("Daily");
        answer.GetProperty("valueDescriptions")[0].GetString().ShouldBe("Every weekday at 09:00");
        fixture.Broker.LastRequest!.Questions[0].Options[0].Value.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("time", "9 o'clock")]
    [InlineData("date", "2030-13-01")]
    [InlineData("recurrence", "{\"frequency\":\"Weekly\",\"start\":\"2030-01-07\",\"time\":\"25:00\"}")]
    public async Task AskUserShouldRefuseAPresetThePickerCouldNotReturn(string kind, string value)
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[] { new { id = "q", text = "When?", pickerKind = kind, options = new[] { new { label = "Preset", value } } } }
        }, expectError: true);

        result.GetProperty("outcome").GetString().ShouldBe("invalid");
        result.GetProperty("error").GetString()!.ShouldContain("not a valid " + kind);
        fixture.Broker.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task AppScheduleShouldTurnTheChatIntoAScheduledChatAndReadItBack()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        var settings = new
        {
            task = "Summarize yesterday's commits",
            recurrence = JsonDocument.Parse(WeekdaysAtNine).RootElement,
            timeZone = "UTC",
            successCriteria = "A summary was written"
        };

        var set = await AppFixture.CallAsync(session, "app_schedule", new { operation = "Set", operationId = Guid.NewGuid(), settings });
        set.GetProperty("applied").GetBoolean().ShouldBeTrue();
        set.GetProperty("effect").GetString().ShouldBe("Scheduled the chat: Every weekday at 09:00.");
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Kind.ShouldBe("scheduled");
        chat.Messages.ShouldHaveSingleItem().Content.ShouldBe("Summarize yesterday's commits");

        var read = await AppFixture.CallAsync(session, "app_schedule", new { operation = "Get" });
        var current = read.GetProperty("current");
        current.GetProperty("description").GetString().ShouldBe("Every weekday at 09:00");
        current.GetProperty("hostTimeZone").GetString().ShouldNotBeNullOrWhiteSpace();
        current.GetProperty("upcoming").GetArrayLength().ShouldBe(3);
        var revision = read.GetProperty("revision").GetInt64();

        var stale = await AppFixture.CallAsync(session, "app_schedule",
            new { operation = "Pause", operationId = Guid.NewGuid(), revision = revision + 5 }, expectError: true);
        stale.GetProperty("status").GetString().ShouldBe("Conflict");
        var paused = await AppFixture.CallAsync(session, "app_schedule", new { operation = "Pause", operationId = Guid.NewGuid(), revision });
        paused.GetProperty("current").GetProperty("schedule").GetProperty("paused").GetBoolean().ShouldBeTrue();

        // Only a run branch reports a run.
        var report = await AppFixture.CallAsync(session, "app_schedule",
            new { operation = "ReportRun", operationId = Guid.NewGuid(), succeeded = true, summary = "Done" }, expectError: true);
        report.GetProperty("error").GetString()!.ShouldContain("not a scheduled run");

        var removed = await AppFixture.CallAsync(session, "app_schedule", new { operation = "Remove", operationId = Guid.NewGuid() });
        removed.GetProperty("applied").GetBoolean().ShouldBeTrue();
        (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None))!.Kind.ShouldBe("conversation");
    }

    [Fact]
    public async Task AppScheduleShouldRefuseAScheduleWithMissingValuesInWordsTheModelCanFix()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var result = await AppFixture.CallAsync(session, "app_schedule", new
        {
            operation = "Set", operationId = Guid.NewGuid(),
            settings = new { task = "Ping", recurrence = new { frequency = "Weekly", start = "tomorrow", time = "09:00" }, timeZone = "UTC" }
        }, expectError: true);

        result.GetProperty("error").GetString()!.ShouldContain("'start' must be a date in the form yyyy-MM-dd");
        (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None))!.Kind.ShouldBe("conversation");
    }
}
