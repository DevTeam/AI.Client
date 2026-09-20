namespace AI.Client.Application.Chat;

public interface IChatTransportPolicy
{
    TimeSpan ResponseHeadersTimeout { get; }
    TimeSpan FirstTokenTimeout { get; }
    TimeSpan RetryDeadline { get; }
}
