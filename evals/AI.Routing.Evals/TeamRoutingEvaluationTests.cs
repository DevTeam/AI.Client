namespace AI.Application.Tests.Skills;

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AI.Application.Chat;
using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Settings;
using AI.Application.Skills;
using AI.Application.Tools;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

/// <summary>
/// Whether the real skill router offers team-assemble for work a team pays off on, and only for
/// that. Paid and opt-in, like the context evaluation; see docs/34-asides-and-team-messages.md.
/// </summary>
public sealed class TeamRoutingEvaluationTests(ITestOutputHelper output)
{
    private const string Team = "team-assemble";
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };

    /// <summary>A message, and whether team-assemble should be the router's first choice for it.</summary>
    private static readonly (string Message, bool Team)[] Cases =
    [
        ("Add a CSV export of orders: a REST endpoint, a download button in the web UI, and tests for both.", true),
        ("Сделай уведомления: сервис на бэкенде, раздел в настройках UI, миграцию базы и тесты.", true),
        ("Implement multi-tenant support across the API, the background workers and the admin UI.", true),
        ("Research Serilog, NLog and Microsoft.Extensions.Logging separately for our needs and recommend one.", false),
        ("Let's do this as a team with roles: rework the auth module and update its documentation.", true),
        ("Распараллель работу: один делает клиент, другой сервер, третий пишет тесты для синхронизации заметок.", true),
        ("Repair independent synchronization defects in the upload service and the web editor; agree on their shared retry contract and coordinate both fixes through integration.", true),
        ("Почини независимые ошибки в сервисе оплаты и интерфейсе возвратов, согласуй общий контракт статусов и проверь их совместную работу.", true),
        ("Compare the supported file formats of three libraries and return a short table.", false),
        ("Распараллель независимую проверку трёх модулей и собери найденные ошибки в один отчёт.", false),
        ("Rename OldClock to SystemClock consistently across the solution without changing its behavior.", false),
        ("Implement the API and its UI yourself, sequentially, without a team or subtasks.", false),
        ("Fix the null reference in OrderService.Calculate.", false),
        ("Rename the variable count to total in Program.cs.", false),
        ("What does ChatRunDispatcher do?", false),
        ("Add a unit test for ParseDate.", false),
        ("Write the commit message for my changes.", false),
        ("Create a sequence diagram of the login flow.", false),
        ("Объясни, как работает очередь сообщений.", false),
        ("Почини падающую сборку: ошибка CS0246 в AppRunsTool.cs.", false),
    ];

    [Fact]
    [Trait("Category", "ModelEvaluation")]
    public async Task ShouldOfferTeamWorkExactlyWhereItPaysOff()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("AI_ROUTING_EVAL") == "1",
            "Set AI_ROUTING_EVAL=1 and a dedicated endpoint/model to run model evaluations.");
        var url = Environment.GetEnvironmentVariable("AI_ROUTING_EVAL_BASE_URL");
        var model = Environment.GetEnvironmentVariable("AI_ROUTING_EVAL_MODEL");
        Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var endpoint) && endpoint.Scheme is "http" or "https",
            "AI_ROUTING_EVAL_BASE_URL must be an absolute HTTP or HTTPS URL.");
        Assert.False(string.IsNullOrWhiteSpace(model), "AI_ROUTING_EVAL_MODEL is required.");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var key = Environment.GetEnvironmentVariable("AI_ROUTING_EVAL_API_KEY");
        if (!string.IsNullOrWhiteSpace(key)) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var routing = CreateRouting(new HttpCompletion(http, url!, model!), url!, model!);
        var run = new ToolRunContext(ProjectId, ChatId, ChatId, true);

        var reports = new List<object>();
        var errors = new List<string>();
        var cases = Cases.Select(item => (item.Message, item.Team, Active: (string?)null)).Concat(
        [
            ("Also add the API, worker and settings page to this feature; agree their contracts and implement everything.", false, "code-feature-implement"),
            ("Within my assigned backend paths, investigate the parser and cache independently, then implement my service.", false, "team-contribute"),
            ("Use subtasks to review independent contract boundaries in my assignment and return one integrated result to the lead.", false, "team-contribute"),
            ("Research two independent integration questions while the teammates work in their own paths.", false, "team-coordinate"),
            ("Extend the plan to cover the API, UI and background service; do not implement it yet.", false, "code-plan")
        ]).ToArray();
        foreach (var (message, team, active) in cases)
        {
            IReadOnlyList<ChatCompletionMessage> context = active is null
                ? [new("user", message)]
                : [new("user", "Continue the assigned task."),
                    new("assistant", string.Empty,
                        [new("skill", "mcp_app__run_skill", JsonSerializer.Serialize(new { skillId = active, parameters = new { } }))]),
                    new("tool", "{\"status\":\"Completed\"}", ToolCallId: "skill"),
                    new("assistant", "Continuing the current assignment."), new("user", message)];
            var clock = Stopwatch.StartNew();
            var route = await routing.RouteAsync(run, context, [],
                TestContext.Current.CancellationToken);
            var skills = route?.Skills.Select(skill => skill.Id).ToArray() ?? [];
            var first = skills.FirstOrDefault() == Team;
            reports.Add(new { message, active, expectedTeam = team, routed = route is not null,
                durationMs = clock.ElapsedMilliseconds, skills });
            if (route is null)
            {
                errors.Add($"Routing did not complete in {clock.ElapsedMilliseconds} ms: \"{message}\".");
                continue;
            }
            if (first != team)
                errors.Add($"{(team ? "missed" : "unwanted")} team: \"{message}\" -> [{string.Join(", ", skills)}]");
            if (active is not null && (skills.Length != 1 || skills[0] != active))
                errors.Add($"Lost active skill {active}: \"{message}\" -> [{string.Join(", ", skills)}]");
        }

        var expected = cases.Count(item => item.Item2);
        var offered = reports.Count - errors.Count;
        output.WriteLine(JsonSerializer.Serialize(new { model, cases = reports.Count, expected, correct = offered, reports },
            ReportOptions));
        errors.ShouldBeEmpty();
    }

    private static readonly Guid ProjectId = Guid.CreateVersion7();
    private static readonly Guid ChatId = Guid.CreateVersion7();
    private static readonly Guid ConnectionId = Guid.CreateVersion7();

    private static SkillRouting CreateRouting(IChatCompletionClient completion, string url, string model)
    {
        var now = DateTimeOffset.UtcNow;
        var chats = new Mock<IChatService>(MockBehavior.Strict);
        chats.Setup(item => item.GetAsync(ProjectId, ChatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatDetails(ChatId, ProjectId, "Chat", now, now, 1, ConnectionId, []));
        var projects = new Mock<IProjectService>(MockBehavior.Strict);
        projects.Setup(item => item.GetAsync(ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync((ProjectDetails?)null);
        var settings = new Mock<IGlobalSettingsRepository>(MockBehavior.Strict);
        settings.Setup(item => item.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalSettings([new ConnectionSettings(ConnectionId, "Evaluation", url, model, true, true, false)],
                [], []) { ChatAutomation = new ChatAutomationSettings(RouteSkills: true) });
        var secrets = new Mock<IGlobalSecretStore>(MockBehavior.Strict);
        secrets.Setup(item => item.GetAsync("connection", ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        var catalog = new BuiltInSkillCatalog();
        var guide = new SkillGuide(catalog);
        var estimator = new ContextTokenEstimator();
        var limits = new ConnectionContextLimitsResolver();
        var policy = new AdaptiveContextPolicy(estimator, limits);
        var planner = new ChatContextPlanner(estimator, new ChatContextCompactor(estimator,
            new ContextSummaryWriter(estimator, new ToolResultContextProjector(), policy), new ToolResultContextProjector()),
            limits, policy);
        var skill = new SkillRouteSkill(catalog, guide, new SkillCandidateSelector(), chats.Object, projects.Object, settings.Object, secrets.Object,
            completion, planner, NullLogger<SkillRouteSkill>.Instance);
        return new SkillRouting(new SkillRunner(catalog, null!, skillRouteSkill: skill), guide, settings.Object);
    }

    /// <summary>Sends the router's request as it is built, without streaming, and hands back the answer.</summary>
    private sealed class HttpCompletion(HttpClient http, string url, string model) : IChatCompletionClient
    {
        public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(ChatCompletionRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var messages = request.ContextMessages ?? [new ChatCompletionMessage("user", request.Message)];
            using var response = await http.PostAsJsonAsync(url.TrimEnd('/') + "/chat/completions",
                new { model, messages = messages.Select(item => new { role = item.Role, content = item.ForModel }), stream = false },
                cancellationToken);
            response.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            yield return new ChatCompletionChunk(
                json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "");
        }

        public Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The router streams its single request.");
    }
}
