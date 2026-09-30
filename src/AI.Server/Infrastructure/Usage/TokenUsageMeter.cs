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
    ILogger<TokenUsageMeter> logger) : ITokenUsageMeter
{
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
        try
        {
            cost = measurement.ReportedCost ?? await PriceAsync(measurement);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            NotRecorded(logger, "Price", error);
            cost = null;
        }

        var record = new TokenUsageRecord(ids.Create(), clock.UtcNow,
            Find(scope => scope.Purpose) ?? TokenUsagePurpose.Direct,
            measurement.Model, measurement.ConnectionId,
            Find(scope => scope.ProjectId), Find(scope => scope.ChatId), Find(scope => scope.BranchId), Find(scope => scope.TurnId),
            measurement.Tokens, measurement.Estimated,
            (long)measurement.Duration.TotalMilliseconds,
            measurement.FirstToken is { } first ? (long)first.TotalMilliseconds : null,
            cost);
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
