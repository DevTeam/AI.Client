namespace AI.Web.Tests.Components;

using AI.Contracts.Chats;
using AI.Contracts.Plans;
using AI.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using Shouldly;
using Xunit;

public sealed class ChatPlanCardRenderingTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ShouldShowTheStepInProgressAndTheWeightedProgressWhenCollapsed()
    {
        var plan = new ChatPlan("Fix the parser",
        [
            new("Study the parser", 1, ChatPlanStepStatus.Completed, 100, StartedAt: Start, FinishedAt: Start.AddMinutes(2)),
            new("Write the fix", 3, ChatPlanStepStatus.InProgress, 50, "Halfway", Start.AddMinutes(2)),
            new("Run the tests", 2, ChatPlanStepStatus.Skipped, Note: "No tests cover it")
        ], Start.AddMinutes(3));

        var html = await RenderAsync(plan);

        html.ShouldContain("class=\"chat-plan chat-summary-card");
        html.ShouldContain("is-collapsed");
        html.ShouldContain("<span class=\"chat-summary-title chat-card-title\">Write the fix</span>");
        html.ShouldContain("1/3");
        html.ShouldContain("62%");
        html.ShouldContain("aria-valuenow=\"62\"");
        html.ShouldContain("--plan-progress: 0.625");
        html.ShouldContain("class=\"chat-plan-step is-skipped ");
        html.ShouldContain("No tests cover it");
        html.ShouldContain(" inert");
        // A settled card has no clock running: no estimate, and the in-progress step has no time yet.
        html.ShouldNotContain("left</span>");
        html.ShouldContain("<span class=\"chat-plan-step-time\" title=\"Working time taken; about 2m expected\">2m</span>");
    }

    [Fact]
    public async Task ShouldSayHowAFinishedPlanEnded()
    {
        var plan = new ChatPlan("Fix", [new("Study", Status: ChatPlanStepStatus.Completed), new("Ship", Status: ChatPlanStepStatus.Failed)], Start);

        var html = await RenderAsync(plan);

        html.ShouldContain("Fix <span class=\"chat-plan-outcome\">· 1 failed</span>");
        html.ShouldContain("100%");
    }

    [Fact]
    public async Task ShouldShowTheCriteriaOfDoneWithTheirStandingAndEvidence()
    {
        var evidenceId = Guid.NewGuid();
        var plan = new ChatPlan("Fix", [new("Study", Status: ChatPlanStepStatus.Completed)], Start, 0,
        [
            new("The parser tests pass", ChatPlanCriterionStatus.Met, "12 of 12 passed", "call-tests"),
            new("Both themes checked", ChatPlanCriterionStatus.NotApplicable, "No UI changed"),
            new("The docs mention the flag")
        ]);

        var html = await RenderAsync(plan, evidence: call => call == "call-tests" ? evidenceId : null);

        // One of the two that apply is met; the one left unchecked after the steps turns the count to a warning.
        html.ShouldContain("class=\"chat-plan-criteria-count is-unverified\"");
        html.ShouldContain("✓ 1/2");
        html.ShouldContain(">Done when</div>");
        html.ShouldContain("class=\"chat-plan-criterion is-not-applicable\"");
        html.ShouldContain("No UI changed");
        html.ShouldContain(">Evidence</button>");
        html.ShouldContain(">Not verified</span>");
    }

    [Fact]
    public void ShouldFindTheResultOfAToolCall()
    {
        var result = new ChatMessageView(Guid.NewGuid(), null, "Tool", "x", Start, ToolCallId: "call-1");
        var feed = new ChatFeed();

        feed.ToolResultOf([new ChatMessageView(Guid.NewGuid(), null, "User", "x", Start), result], "call-1").ShouldBe(result.Id);
        feed.ToolResultOf([result], "call-2").ShouldBeNull();
    }

    [Fact]
    public async Task ShouldForecastALivePlanAndSayWhenAStepRunsOverOrTheRunWaits()
    {
        var now = DateTimeOffset.UtcNow;
        var plan = new ChatPlan("Fix",
        [
            new("Study", 1, ChatPlanStepStatus.InProgress, 50, Minutes: 2, StartedAt: now.AddMinutes(-10), StartedClock: 0),
            new("Build", 1, Minutes: 5)
        ], now, 600);

        var working = await RenderAsync(plan, live: true);
        var waiting = await RenderAsync(plan, live: true, waiting: true);

        working.ShouldContain("left</span>");
        working.ShouldContain("class=\"chat-plan-step is-in-progress is-over\"");
        working.ShouldContain(">10m / ~2m</span>");
        working.ShouldContain(">~5m</span>");
        waiting.ShouldContain("Waiting for you");
        waiting.ShouldNotContain("left</span>");
    }

    [Theory]
    [InlineData(42, "42s")]
    [InlineData(600, "10m")]
    [InlineData(3_900, "1h 5m")]
    [InlineData(7_200, "2h")]
    public void ShouldFormatDurationsAtTheirScale(int seconds, string expected) =>
        ChatPlanCard.FormatDuration(TimeSpan.FromSeconds(seconds)).ShouldBe(expected);

    [Fact]
    public void ShouldFindTheNewestPlanAndWhetherTheLastTurnSetIt()
    {
        var feed = new ChatFeed();
        var first = new ChatPlan(null, [new("A")], Start);
        var second = new ChatPlan(null, [new("B")], Start);
        ChatMessageView Message(string role, ChatPlan? plan = null) =>
            new(Guid.NewGuid(), null, role, "x", Start, Plan: plan);

        feed.LatestPlan([Message("User"), Message("Tool", first), Message("Tool", second), Message("Assistant")])
            .ShouldBe(new PlanSighting(second, true));
        feed.LatestPlan([Message("User"), Message("Tool", first), Message("Assistant"), Message("User")])
            .ShouldBe(new PlanSighting(first, false));
        feed.LatestPlan([Message("User"), Message("Assistant")]).ShouldBeNull();
    }

    private static async Task<string> RenderAsync(ChatPlan plan, bool live = false, bool waiting = false,
        Func<string, Guid?>? evidence = null)
    {
        var composition = new Composition("http://127.0.0.1:52173/", publicWeb: true);
        var registrations = new ServiceCollection();
        registrations.AddTransient<AI.Contracts.Navigation.IAppNavigationTargets, AI.Contracts.Navigation.AppNavigationTargets>();
        registrations.AddTransient<AI.Web.Navigation.IAppControlHints, AI.Web.Navigation.AppControlHints>();
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = (ServiceProvider)composition.CreateServiceProvider(composition.CreateBuilder(registrations));
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<ChatPlanCard>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(ChatPlanCard.Plan)] = plan,
                    [nameof(ChatPlanCard.Live)] = live,
                    [nameof(ChatPlanCard.WorkingSince)] = live ? plan.UpdatedAt : null,
                    [nameof(ChatPlanCard.Waiting)] = waiting,
                    [nameof(ChatPlanCard.EvidenceMessage)] = evidence,
                    [nameof(ChatPlanCard.OnShowMessage)] = EventCallback.Factory.Create<Guid>(new object(), (Guid _) => { })
                }));
            return component.ToHtmlString();
        });
    }
}
