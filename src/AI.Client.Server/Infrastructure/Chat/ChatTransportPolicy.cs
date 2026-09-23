namespace AI.Client.Infrastructure.Chat;

using AI.Client.Application.Chat;

public sealed class ChatTransportPolicy(
    TimeSpan? responseHeadersTimeout = null,
    TimeSpan? firstTokenTimeout = null,
    TimeSpan? retryDeadline = null) : IChatTransportPolicy
{
    public TimeSpan ResponseHeadersTimeout { get; } = responseHeadersTimeout ?? TimeSpan.FromSeconds(30);
    public TimeSpan FirstTokenTimeout { get; } = firstTokenTimeout ?? TimeSpan.FromSeconds(90);
    public TimeSpan RetryDeadline { get; } = retryDeadline ?? TimeSpan.FromMinutes(10);
}
