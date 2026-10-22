namespace AI.Application.Usage;

/// <summary>
/// Attributes model requests to what they were made for. Scopes follow the asynchronous flow, so a
/// request made anywhere below a scope — a nested run, a skill, a summarizer — is recorded with it
/// without that code knowing it is being measured.
/// </summary>
public interface ITokenUsageMeter
{
    IDisposable Begin(TokenUsageScope scope);

    /// <summary>
    /// Records <paramref name="measurement"/> under the current scope. Never fails the request it
    /// measures: a ledger that cannot be written is logged, not thrown.
    /// </summary>
    Task RecordAsync(TokenUsageMeasurement measurement, CancellationToken cancellationToken);
}
