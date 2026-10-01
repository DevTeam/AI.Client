namespace AI.Application.Chat;

public interface IChatTransportPolicy
{
    TimeSpan ResponseHeadersTimeout { get; }
    TimeSpan FirstTokenTimeout { get; }

    /// <summary>
    /// How long a stream that has started may go without a single line before it counts as stalled.
    /// </summary>
    TimeSpan StreamIdleTimeout { get; }
    TimeSpan RetryDeadline { get; }
}
