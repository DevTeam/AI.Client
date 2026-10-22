namespace AI.Application.Usage;

using AI.Contracts.Usage;

/// <summary>Every measured request, kept for as long as the data directory is.</summary>
public interface ITokenUsageLedger
{
    Task AppendAsync(TokenUsageRecord record, CancellationToken cancellationToken);

    /// <summary>Records made at or after <paramref name="from"/> and before <paramref name="until"/>, oldest first.</summary>
    Task<IReadOnlyList<TokenUsageRecord>> ReadAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken cancellationToken);
}
