namespace AI.Infrastructure.Tests.Tools;

using AI.Contracts.Runs;
using Shouldly;
using Xunit;

/// <summary>The value-like inputs of ask_user, its defaults and its "remember my answer", through the in-process MCP transport.</summary>
public sealed partial class AppToolTests
{
    [Fact]
    public async Task AskUserShouldReturnANumberOnItsStepWithItsUnitInWords()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        fixture.Broker.Answer = _ => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered,
            [new UserPromptAnswer("timeout", [], null, Values: ["1500"])]);

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[]
            {
                new
                {
                    id = "timeout", text = "Request timeout?", options = Array.Empty<object>(),
                    input = new { kind = "number", min = 100, max = 5000, step = 100, unit = "ms" }, @default = 1000,
                    preview = "≈ {value / 1000:1} s per request"
                }
            }
        });

        var answer = result.GetProperty("answers")[0];
        answer.GetProperty("number").GetDouble().ShouldBe(1500);
        answer.GetProperty("values")[0].GetString().ShouldBe("1500");
        answer.GetProperty("valueDescriptions")[0].GetString().ShouldBe("1,500 ms");
        answer.TryGetProperty("assumed", out _).ShouldBeFalse();
        var asked = fixture.Broker.LastRequest!.Questions[0];
        asked.Input!.Kind.ShouldBe(UserPromptInputKind.Number);
        asked.Defaults.ShouldBe(["1000"]);
        // A slider is the whole answer: there is no free-text box beside it for the composer to fill.
        asked.AllowOther.ShouldBeFalse();
    }

    [Fact]
    public async Task AskUserShouldDropANumberOffItsSliderAndOrderTheEndsOfARange()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        fixture.Broker.Answer = _ => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered,
        [
            new UserPromptAnswer("count", [], null, Values: ["11"]),
            new UserPromptAnswer("window", [], null, Values: ["18", "9"])
        ]);

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new object[]
            {
                new { id = "count", text = "How many?", options = Array.Empty<object>(), input = new { kind = "number", min = 0, max = 10 } },
                new { id = "window", text = "Working hours?", options = Array.Empty<object>(), input = new { kind = "range", min = 0, max = 24, unit = "h" } }
            }
        });

        var answers = result.GetProperty("answers").EnumerateArray().ToArray();
        // 11 is off a 0..10 slider: not an answer the card could have given, so it is not one.
        answers.ShouldHaveSingleItem().GetProperty("id").GetString().ShouldBe("window");
        answers[0].GetProperty("range").GetProperty("from").GetDouble().ShouldBe(9);
        answers[0].GetProperty("range").GetProperty("to").GetDouble().ShouldBe(18);
        answers[0].GetProperty("valueDescriptions")[0].GetString().ShouldBe("9–18 h");
        result.GetProperty("guidance").GetString()!.ShouldContain("count");
    }

    [Fact]
    public async Task AskUserShouldReturnEveryOptionOfARankInTheOrderGiven()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        fixture.Broker.Answer = _ => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered,
            [new UserPromptAnswer("order", [2, 0], null)]);

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[]
            {
                new
                {
                    id = "order", text = "What first?", input = new { kind = "rank" },
                    options = new[] { new { label = "Tests" }, new { label = "Refactor" }, new { label = "Feature" } }
                }
            }
        });

        var answer = result.GetProperty("answers")[0];
        answer.GetProperty("selected").EnumerateArray().Select(item => item.GetString()).ShouldBe(["Feature", "Tests", "Refactor"]);
        answer.GetProperty("ranked").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task AskUserLeftToTheModelShouldTakeItsDefaultsAndSayWhichHadNone()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        fixture.Broker.Answer = _ => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Dismissed, []);

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new object[]
            {
                new { id = "scope", text = "How far?", options = new[] { new { label = "Narrow", recommended = false }, new { label = "Wide", recommended = true } } },
                new { id = "colour", text = "Accent?", options = Array.Empty<object>(), input = new { kind = "color" }, @default = "#3B82F6" },
                new { id = "name", text = "Called?", options = Array.Empty<object>() }
            }
        });

        result.GetProperty("outcome").GetString().ShouldBe("dismissed");
        var answers = result.GetProperty("answers").EnumerateArray().ToArray();
        answers.Length.ShouldBe(2);
        answers[0].GetProperty("selected")[0].GetString().ShouldBe("Wide");
        answers[0].GetProperty("assumed").GetBoolean().ShouldBeTrue();
        answers[1].GetProperty("values")[0].GetString().ShouldBe("#3b82f6");
        var guidance = result.GetProperty("guidance").GetString()!;
        guidance.ShouldContain("Your defaults were taken for scope, colour");
        guidance.ShouldContain("no default: name");
    }

    [Fact]
    public async Task AskUserShouldAskTheModelToSaveARememberedAnswer()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        fixture.Broker.Answer = _ => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered,
            [new UserPromptAnswer("tests", [1], null, Remember: true)]);

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[]
            {
                new
                {
                    id = "tests", text = "Where do tests go?", rememberable = true,
                    options = new[] { new { label = "Next to the code" }, new { label = "In tests/" } }
                }
            }
        });

        result.GetProperty("answers")[0].GetProperty("remember").GetBoolean().ShouldBeTrue();
        result.GetProperty("guidance").GetString()!.ShouldContain("app_memory");
    }

    [Fact]
    public async Task AskUserShouldDropTextItsPatternRefuses()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        fixture.Broker.Answer = _ => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered,
            [new UserPromptAnswer("version", [], "v1.2")]);

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[]
            {
                new { id = "version", text = "Version?", options = Array.Empty<object>(), input = new { kind = "text", pattern = @"\d+\.\d+\.\d+" } }
            }
        });

        result.GetProperty("answers").GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [InlineData("""{"kind":"slider","min":0,"max":1}""", "must be one of")]
    [InlineData("""{"kind":"number","max":10}""", "needs finite 'min' and 'max'")]
    [InlineData("""{"kind":"scale","min":1,"max":20}""", "at most 10 apart")]
    [InlineData("""{"kind":"text","pattern":"("}""", "not a valid regular expression")]
    public async Task AskUserShouldExplainAnInputItCannotDraw(string input, string message)
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[]
            {
                new { id = "q", text = "Which?", options = Array.Empty<object>(), input = System.Text.Json.JsonElement.Parse(input) }
            }
        }, expectError: true);

        result.GetProperty("outcome").GetString().ShouldBe("invalid");
        result.GetProperty("error").GetString()!.ShouldContain(message);
    }

    [Fact]
    public async Task AskUserShouldRefuseAPreviewWithAnUnknownName()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[]
            {
                new { id = "q", text = "How many?", options = Array.Empty<object>(), input = new { kind = "number", min = 1, max = 9 }, preview = "{files * 2}" }
            }
        }, expectError: true);

        result.GetProperty("error").GetString()!.ShouldContain("unknown name 'files'");
    }
}
