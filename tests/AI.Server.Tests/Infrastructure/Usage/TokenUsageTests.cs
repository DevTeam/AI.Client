namespace AI.Infrastructure.Tests.Usage;

using AI.Application.Chat;
using AI.Application.Projects;
using AI.Application.Settings;
using AI.Application.Usage;
using AI.Contracts.Settings;
using AI.Contracts.Usage;
using AI.Infrastructure.Chat;
using AI.Infrastructure.Storage;
using AI.Infrastructure.Tests.Storage;
using AI.Infrastructure.Usage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using System.Runtime.CompilerServices;
using Xunit;

public class TokenUsageTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ChatId = Guid.NewGuid();
    private static readonly Guid ConnectionId = Guid.NewGuid();
    private readonly MemoryFileSystem _files = new();
    private readonly Mock<IGlobalSettingsRepository> _settings = new();
    private readonly PromptPrefixTracker _prefixes = new(new ContextTokenEstimator());
    private DateTimeOffset _now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    public TokenUsageTests() => _settings.Setup(item => item.LoadAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new GlobalSettings([], [], []));

    [Fact]
    public async Task ShouldAttributeARequestToTheScopesAroundIt()
    {
        var (meter, ledger) = CreateMeter();
        var turn = Guid.NewGuid();
        var observed = new List<TokenUsageRecord>();

        using (meter.Begin(new TokenUsageScope(TokenUsagePurpose.Answer, ProjectId, ChatId, ChatId, turn,
                   (record, _) => { observed.Add(record); return Task.CompletedTask; })))
        using (meter.Begin(new TokenUsageScope(TokenUsagePurpose.Subtask)))
            await meter.RecordAsync(Measurement(new TokenCounts(100, 10)), CancellationToken.None);
        await meter.RecordAsync(Measurement(new TokenCounts(5, 1)), CancellationToken.None);

        var records = await ledger.ReadAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, CancellationToken.None);
        records.Count.ShouldBe(2);
        records[0].Purpose.ShouldBe(TokenUsagePurpose.Subtask);
        records[0].TurnId.ShouldBe(turn);
        records[0].ChatId.ShouldBe(ChatId);
        // Outside every scope a request is still measured, as a direct one.
        records[1].Purpose.ShouldBe(TokenUsagePurpose.Direct);
        records[1].TurnId.ShouldBeNull();
        observed.Single().Id.ShouldBe(records[0].Id);
    }

    [Fact]
    public async Task ShouldPriceUsageWithTheConnectionsPrices()
    {
        _settings.Setup(item => item.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new GlobalSettings(
            [new ConnectionSettings(ConnectionId, "c", "https://llm.example/v1", "m", true, true, false,
                Prices: new TokenPrices(2m, 8m, 0.5m))], [], []));
        var (meter, ledger) = CreateMeter();

        await meter.RecordAsync(Measurement(new TokenCounts(1_000_000, 500_000, 400_000)), CancellationToken.None);
        await meter.RecordAsync(Measurement(new TokenCounts(10, 10)) with { ReportedCost = 0.01m }, CancellationToken.None);

        var records = await ledger.ReadAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, CancellationToken.None);
        // 600k fresh input at 2, 400k cached at 0.5, 500k output at 8.
        records[0].Cost.ShouldBe(1.2m + 0.2m + 4m);
        records[1].Cost.ShouldBe(0.01m);
    }

    [Fact]
    public async Task ShouldKeepRecordingWhenTheLedgerCannotBeWritten()
    {
        var ledger = new Mock<ITokenUsageLedger>();
        ledger.Setup(item => item.AppendAsync(It.IsAny<TokenUsageRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk full"));
        var meter = new TokenUsageMeter(ledger.Object, _settings.Object, Clock(), Ids(), NullLogger<TokenUsageMeter>.Instance,
            new PromptPrefixTracker(new ContextTokenEstimator()), new UsageCostEstimator());
        var observed = 0;

        using (meter.Begin(new TokenUsageScope(Observer: (_, _) => { observed++; return Task.CompletedTask; })))
            await meter.RecordAsync(Measurement(new TokenCounts(1, 1)), CancellationToken.None);

        observed.ShouldBe(1);
    }

    [Fact]
    public async Task ShouldMeasureAStreamByItsReportAndEstimateOneWithout()
    {
        var (meter, ledger) = CreateMeter();
        var request = new ChatCompletionRequest("https://llm.example/v1", "m", null, new string('x', 400), ConnectionId);

        await DrainAsync(new MeteringChatCompletionClient(new FakeClient(
            new ChatCompletionChunk("Hello", "m-2026"),
            new ChatCompletionChunk("", "m-2026", Usage: new ChatCompletionUsage(new TokenCounts(42, 3)))), meter,
            new ContextTokenEstimator(), _prefixes), request);
        await DrainAsync(new MeteringChatCompletionClient(new FakeClient(new ChatCompletionChunk("Hello")), meter,
            new ContextTokenEstimator(), _prefixes), request);

        var records = await ledger.ReadAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, CancellationToken.None);
        records[0].Estimated.ShouldBeFalse();
        records[0].Tokens.ShouldBe(new TokenCounts(42, 3));
        records[0].Model.ShouldBe("m-2026");
        records[0].ConnectionId.ShouldBe(ConnectionId);
        records[0].FirstTokenMs.ShouldNotBeNull();
        records[1].Estimated.ShouldBeTrue();
        records[1].Model.ShouldBe("m");
        records[1].Tokens.InputTokens.ShouldBeGreaterThan(100);
        records[1].Tokens.OutputTokens.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ShouldRecordAStreamStoppedPartWay()
    {
        var (meter, ledger) = CreateMeter();
        var client = new MeteringChatCompletionClient(new FakeClient(new ChatCompletionChunk("a"), new ChatCompletionChunk("b")),
            meter, new ContextTokenEstimator(), _prefixes);

        await foreach (var _ in client.StreamAsync(new ChatCompletionRequest("https://llm.example/v1", "m", null, "Hi"),
                           CancellationToken.None))
            break;

        (await ledger.ReadAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, CancellationToken.None))
            .Single().Estimated.ShouldBeTrue();
    }

    [Fact]
    public async Task ShouldReadRecordsBackByMonthAndSkipALineCutShort()
    {
        var (meter, _) = CreateMeter();
        _now = new DateTimeOffset(2026, 8, 31, 23, 0, 0, TimeSpan.Zero);
        await meter.RecordAsync(Measurement(new TokenCounts(1, 1)), CancellationToken.None);
        _now = new DateTimeOffset(2026, 9, 1, 1, 0, 0, TimeSpan.Zero);
        await meter.RecordAsync(Measurement(new TokenCounts(2, 2)), CancellationToken.None);
        var september = _files.Files.Keys.Single(path => path.EndsWith("2026-09.jsonl", StringComparison.Ordinal));
        _files.Files[september] += "{\"id\":\"broken";

        var fresh = new JsonLinesTokenUsageLedger(Location(), _files);
        var all = await fresh.ReadAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, CancellationToken.None);
        var august = await fresh.ReadAsync(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        all.Select(record => record.Tokens.InputTokens).ShouldBe([1L, 2L]);
        august.Single().Tokens.InputTokens.ShouldBe(1);
        _files.Files.Keys.ShouldAllBe(path => path.Contains(Path.Combine("root", "usage"), StringComparison.Ordinal));
    }

    [Fact]
    public void ShouldGroupAChatsRequestsIntoTurns()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var records = new[]
        {
            Record(first, TokenUsagePurpose.Routing, 100, 5, cost: 0.001m, estimated: true),
            Record(first, TokenUsagePurpose.Answer, 1_000, 50, cost: 0.01m),
            Record(first, TokenUsagePurpose.Subtask, 3_000, 200),
            Record(null, TokenUsagePurpose.Title, 200, 10),
            Record(second, TokenUsagePurpose.Answer, 2_000, 80),
            Record(second, TokenUsagePurpose.Answer, 9, 9) with { ChatId = Guid.NewGuid() }
        };

        var usage = new TokenUsageAggregator().Chat(ProjectId, ChatId, records);

        usage.Totals.Requests.ShouldBe(5);
        usage.Totals.Tokens.InputTokens.ShouldBe(6_300);
        usage.Totals.Cost.ShouldBe(0.011m);
        usage.Totals.PricedRequests.ShouldBe(2);
        usage.Totals.EstimatedRequests.ShouldBe(1);
        usage.Turns.Select(turn => turn.TurnId).ShouldBe([first, second]);
        usage.Turns[0].Totals.Requests.ShouldBe(3);
        usage.Turns[0].ByPurpose[0].Key.ShouldBe(nameof(TokenUsagePurpose.Subtask));
        usage.ByPurpose.Select(slice => slice.Key).ShouldContain(nameof(TokenUsagePurpose.Title));
    }

    [Fact]
    public void ShouldReportAPeriodByDayModelAndPurpose()
    {
        var records = new[]
        {
            Record(null, TokenUsagePurpose.Answer, 10, 1) with { At = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero) },
            Record(null, TokenUsagePurpose.Answer, 20, 2) with { At = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero), Model = "other" },
            Record(null, TokenUsagePurpose.Title, 30, 3) with { At = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero) }
        };

        var report = new TokenUsageAggregator().Report(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), records);

        report.Totals.Requests.ShouldBe(2);
        report.ByDay.Select(day => day.Key).ShouldBe(["2026-09-01", "2026-09-02"]);
        report.ByModel.Select(model => model.Key).ShouldBe(["other", "m"]);
        report.ByPurpose.Single().Key.ShouldBe(nameof(TokenUsagePurpose.Answer));
    }

    [Fact]
    public async Task ShouldSayWhatChangedAtTheStartOfARequestSinceThePreviousOne()
    {
        var (meter, ledger) = CreateMeter();
        var schema = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone();
        ChatToolDefinition[] tools = [new("read", "Read a file", schema)];
        ChatCompletionMessage[] start = [new("system", "Base"), new("user", "Question " + new string('q', 2_000))];
        async Task SendAsync(IReadOnlyList<ChatCompletionMessage> messages, IReadOnlyList<ChatToolDefinition> sent)
        {
            using var scope = meter.Begin(new TokenUsageScope(TokenUsagePurpose.Answer, ProjectId, ChatId, ChatId));
            await DrainAsync(new MeteringChatCompletionClient(new FakeClient(new ChatCompletionChunk("ok", "m",
                    Usage: new ChatCompletionUsage(new TokenCounts(1_000, 1)))), meter, new ContextTokenEstimator(), _prefixes),
                new ChatCompletionRequest("https://llm.example/v1", "m", null, "", ConnectionId, messages, sent));
        }

        await SendAsync(start, tools);
        // Carried on, with this step's guidance at the end.
        await SendAsync([.. start, new("assistant", "Answer"), new("user", "Guidance")], tools);
        // The guidance went and the conversation carried on: still a continuation.
        await SendAsync([.. start, new("assistant", "Answer"), new("user", "Next")], tools);
        await SendAsync([new("system", "Base, changed"), start[1], new("assistant", "Answer"), new("user", "Next")], tools);
        await SendAsync([new("system", "Base, changed"), new("user", "Summary"), new("user", "Next")], tools);
        await SendAsync([new("system", "Base, changed"), new("user", "Summary"), new("user", "Next")], []);

        var records = await ledger.ReadAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, CancellationToken.None);
        records[0].Prefix.ShouldBeNull();
        records[1].Prefix.ShouldNotBeNull().Change.ShouldBeNull();
        records[1].Prefix!.ReusableTokens.ShouldBeGreaterThan(1_000);
        records[2].Prefix!.Change.ShouldBeNull();
        records[3].Prefix!.Change.ShouldBe(PromptPrefixChange.Instructions);
        records[4].Prefix!.Change.ShouldBe(PromptPrefixChange.History);
        records[5].Prefix.ShouldBe(new PromptPrefix(0, PromptPrefixChange.Tools));
        var totals = new TokenUsageAggregator().Total(records);
        (totals.ToolChanges, totals.InstructionChanges, totals.HistoryChanges).ShouldBe((1, 1, 1));
    }

    [Fact]
    public async Task ShouldEstimateAnUnquotedCostFromTheEndpointsEarlierQuotes()
    {
        var (meter, ledger) = CreateMeter();
        // Quoted at 1 per million fresh input, 0.1 cached and 4 output.
        await meter.RecordAsync(Measurement(new TokenCounts(1_000_000, 100_000)) with { ReportedCost = 1.4m }, CancellationToken.None);
        await meter.RecordAsync(Measurement(new TokenCounts(2_000_000, 0, 1_000_000)) with { ReportedCost = 1.1m }, CancellationToken.None);
        await meter.RecordAsync(Measurement(new TokenCounts(500_000, 500_000)) with { ReportedCost = 2.5m }, CancellationToken.None);
        await meter.RecordAsync(Measurement(new TokenCounts(3_000_000, 200_000, 2_000_000)) with { ReportedCost = 2m }, CancellationToken.None);
        await meter.RecordAsync(Measurement(new TokenCounts(2_000_000, 250_000)) with { ReportedCost = 3m }, CancellationToken.None);
        await meter.RecordAsync(Measurement(new TokenCounts(1_000_000, 1_000_000, 500_000)), CancellationToken.None);

        var unquoted = (await ledger.ReadAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, CancellationToken.None))[^1];
        unquoted.CostEstimated.ShouldBeTrue();
        unquoted.Cost.ShouldNotBeNull().ShouldBe(0.5m + 0.05m + 4m, 0.0001m);
    }

    [Fact]
    public async Task ShouldEstimateFromQuotesTheLedgerKeptBeforeTheApplicationStarted()
    {
        var (first, ledger) = CreateMeter();
        await first.RecordAsync(Measurement(new TokenCounts(1_000_000, 0)) with { ReportedCost = 2m }, CancellationToken.None);
        var meter = new TokenUsageMeter(ledger, _settings.Object, Clock(), Ids(), NullLogger<TokenUsageMeter>.Instance,
            new PromptPrefixTracker(new ContextTokenEstimator()), new UsageCostEstimator());

        await meter.RecordAsync(Measurement(new TokenCounts(500_000, 0)), CancellationToken.None);

        var records = await ledger.ReadAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, CancellationToken.None);
        records[^1].Cost.ShouldBe(1m);
        records[^1].CostEstimated.ShouldBeTrue();
        new TokenUsageAggregator().Total(records).EstimatedCostRequests.ShouldBe(1);
    }

    [Fact]
    public async Task ShouldPriceARequestTheEndpointReportedNothingForAndNamedAnotherModel()
    {
        var (meter, ledger) = CreateMeter();
        await meter.RecordAsync(Measurement(new TokenCounts(1_000_000, 0)) with { ReportedCost = 2m, Model = "Vendor-M3" },
            CancellationToken.None);

        await meter.RecordAsync(Measurement(new TokenCounts(250_000, 0)) with { Estimated = true }, CancellationToken.None);

        var unreported = (await ledger.ReadAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, CancellationToken.None))[^1];
        unreported.Cost.ShouldBe(0.5m);
        unreported.CostEstimated.ShouldBeTrue();
    }

    private (TokenUsageMeter Meter, JsonLinesTokenUsageLedger Ledger) CreateMeter()
    {
        var ledger = new JsonLinesTokenUsageLedger(Location(), _files);
        return (new TokenUsageMeter(ledger, _settings.Object, Clock(), Ids(), NullLogger<TokenUsageMeter>.Instance,
            new PromptPrefixTracker(new ContextTokenEstimator()), new UsageCostEstimator()), ledger);
    }

    private IClock Clock()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(item => item.UtcNow).Returns(() => _now);
        return clock.Object;
    }

    private static IIdGenerator Ids()
    {
        var ids = new Mock<IIdGenerator>();
        ids.Setup(item => item.Create()).Returns(Guid.CreateVersion7);
        return ids.Object;
    }

    private static IProjectStorageLocation Location()
    {
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(item => item.RootDirectory).Returns(Path.Combine(Path.GetTempPath(), "root"));
        return location.Object;
    }

    private static TokenUsageMeasurement Measurement(TokenCounts tokens) =>
        new("m", ConnectionId, tokens, false, TimeSpan.FromSeconds(1));

    private static TokenUsageRecord Record(Guid? turn, TokenUsagePurpose purpose, long input, long output,
        decimal? cost = null, bool estimated = false) =>
        new(Guid.CreateVersion7(), new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero), purpose, "m", ConnectionId,
            ProjectId, ChatId, ChatId, turn, new TokenCounts(input, output), estimated, 1_000, null, cost);

    private static async Task DrainAsync(MeteringChatCompletionClient client, ChatCompletionRequest request)
    {
        await foreach (var _ in client.StreamAsync(request, CancellationToken.None)) { }
    }

    private sealed class FakeClient(params ChatCompletionChunk[] chunks) : IChatCompletionClient
    {
        public Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(ChatCompletionRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var chunk in chunks)
            {
                await Task.Yield();
                yield return chunk;
            }
        }
    }
}
