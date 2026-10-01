namespace AI.Application.Usage;

using System.Collections.Concurrent;
using AI.Contracts.Usage;

/// <summary>The latest limits each connection's endpoint reported, for as long as the process runs.</summary>
public interface IConnectionRateLimits
{
    void Record(Guid connectionId, RateLimitStatus status);

    RateLimitStatus? Find(Guid connectionId);
}

public sealed class ConnectionRateLimits : IConnectionRateLimits
{
    private readonly ConcurrentDictionary<Guid, RateLimitStatus> _latest = new();

    public void Record(Guid connectionId, RateLimitStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        _latest[connectionId] = status;
    }

    public RateLimitStatus? Find(Guid connectionId) => _latest.GetValueOrDefault(connectionId);
}
