namespace AI.Client.Contracts.Runs;

public enum ChatRunWaitKind
{
    RateLimit
}

public sealed record ChatRunWait(ChatRunWaitKind Kind, DateTimeOffset RetryAt, int Attempt);
