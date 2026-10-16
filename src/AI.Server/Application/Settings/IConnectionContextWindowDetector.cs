namespace AI.Application.Settings;

using AI.Contracts.Settings;

/// <summary>
/// Learns a connection's context window from its endpoint's model catalog when nobody set one, so a
/// 128K model is not planned as if it had the 32K fallback.
/// </summary>
public interface IConnectionContextWindowDetector
{
    /// <summary>
    /// Returns <paramref name="connection"/> with <see cref="ConnectionSettings.DetectedContextWindow"/>
    /// for its model when the catalog reports one, and unchanged otherwise: when the window is set
    /// by hand, already known, not reported, or the endpoint cannot be reached.
    /// </summary>
    Task<ConnectionSettings> DetectAsync(ConnectionSettings connection, CancellationToken cancellationToken);
}
