namespace AI.Infrastructure.Usage;

using AI.Application.Projects;
using AI.Application.Settings;
using AI.Application.Usage;
using AI.Contracts.Usage;
using Microsoft.Extensions.Logging;

public sealed class TokenUsageMeter(
    ITokenUsageLedger ledger,
    IGlobalSettingsRepository settings,
    IClock clock,
    IIdGenerator ids,
    ILogger<TokenUsageMeter> logger,
    IPromptPrefixTracker prefixes,
    IUsageCostEstimator costs) : ITokenUsageMeter
{
    /// <summary>How far back the quotes the cost estimate starts from are read, once.</summary>
    private static readonly TimeSpan LearnedQuotesAge = TimeSpan.FromDays(31);

    private Task? _learned;

    private static readonly Action<ILogger, string, Exception?> NotRecorded =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1201, "TokenUsageNotRecorded"),
            "TokenUsageNotRecorded Stage={Stage}");

    private readonly AsyncLocal<Frame?> _current = new();

    public IDisposable Begin(TokenUsageScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var frame = new Frame(this, _current.Value, scope);
        _current.Value = frame;
        return frame;
    }

    public async Task RecordAsync(TokenUsageMeasurement measurement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        var frames = Chain(_current.Value).ToArray();
        T? Find<T>(Func<TokenUsageScope, T?> field) where T : struct =>
            frames.Select(frame => field(frame.Scope)).FirstOrDefault(value => value is not null);
        decimal? cost;
        var costEstimated = false;
        try
        {
            cost = measurement.ReportedCost ?? await PriceAsync(measurement);
            if (measurement is { ReportedCost: { } quoted, ConnectionId: { } quotedBy, Estimated: false })
                costs.Learn(quotedBy, measurement.Tokens, quoted);
            // Counts the application estimated are priced the same way; the record says both are estimates.
            else if (cost is null && measurement.ConnectionId is { } connectionId)
            {
                await LearnFromLedgerAsync();
                cost = costs.Estimate(connectionId, measurement.Tokens);
                costEstimated = cost is not null;
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            NotRecorded(logger, "Price", error);
            cost = null;
            costEstimated = false;
        }

        var purpose = Find(scope => scope.Purpose) ?? TokenUsagePurpose.Direct;
        var chatId = Find(scope => scope.ChatId);
        var branchId = Find(scope => scope.BranchId);
        var prefix = measurement.Shape is { } shape && chatId is { } chat
            ? prefixes.Compare(new PromptPrefixKey(chat, branchId, purpose, measurement.ConnectionId, shape.Model, shape.BaseUrl), shape)
            : null;

        var record = new TokenUsageRecord(ids.Create(), clock.UtcNow, purpose,
            measurement.Model, measurement.ConnectionId,
            Find(scope => scope.ProjectId), chatId, branchId, Find(scope => scope.TurnId),
            measurement.Tokens, measurement.Estimated,
            (long)measurement.Duration.TotalMilliseconds,
            measurement.FirstToken is { } first ? (long)first.TotalMilliseconds : null,
            cost, prefix, costEstimated);
        // The request has already happened, whatever the run does next: it is written even when
        // the run is being cancelled, so a stopped turn still shows what it spent.
        try
        {
            await ledger.AppendAsync(record, CancellationToken.None);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            NotRecorded(logger, "Ledger", error);
        }

        foreach (var frame in frames)
        {
            if (frame.Scope.Observer is not { } observer) continue;
            try
            {
                await observer(record, cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                NotRecorded(logger, "Observer", error);
            }
        }
    }

    /// <summary>
    /// The quotes of the last month, read once: a gateway that quotes now and then has usually
    /// quoted something before the application last started.
    /// </summary>
    private Task LearnFromLedgerAsync()
    {
        if (Volatile.Read(ref _learned) is { } learned) return learned;
        var reading = ReadQuotesAsync();
        return Interlocked.CompareExchange(ref _learned, reading, null) ?? reading;
    }

    private async Task ReadQuotesAsync()
    {
        var now = clock.UtcNow;
        try
        {
            foreach (var record in await ledger.ReadAsync(now - LearnedQuotesAge, now.AddMinutes(1), CancellationToken.None))
                if (record is { Cost: { } quoted, CostEstimated: false, Estimated: false, ConnectionId: { } connectionId })
                    costs.Learn(connectionId, record.Tokens, quoted);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Without the old quotes the estimate starts from the ones made from now on.
            NotRecorded(logger, "Quotes", error);
        }
    }

    private async Task<decimal?> PriceAsync(TokenUsageMeasurement measurement)
    {
        if (measurement.ConnectionId is not { } connectionId) return null;
        var global = await settings.LoadAsync(CancellationToken.None);
        if (global.Connections.SingleOrDefault(item => item.Id == connectionId)?.Prices is not { } prices) return null;
        var tokens = measurement.Tokens;
        var cached = Math.Min(tokens.CachedInputTokens, tokens.InputTokens);
        return ((tokens.InputTokens - cached) * prices.Input
                + cached * (prices.CachedInput ?? prices.Input)
                + tokens.OutputTokens * prices.Output) / 1_000_000m;
    }

    private static IEnumerable<Frame> Chain(Frame? frame)
    {
        for (var current = frame; current is not null; current = current.Previous) yield return current;
    }

    private sealed class Frame(TokenUsageMeter owner, Frame? previous, TokenUsageScope scope) : IDisposable
    {
        public Frame? Previous { get; } = previous;

        public TokenUsageScope Scope { get; } = scope;

        public void Dispose()
        {
            if (ReferenceEquals(owner._current.Value, this)) owner._current.Value = Previous;
        }
    }
}
