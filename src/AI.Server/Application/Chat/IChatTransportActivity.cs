namespace AI.Application.Chat;

public interface IChatTransportActivity
{
    IDisposable BeginScope(Func<ChatTransportWait?, CancellationToken, Task> report);
    Task ReportAsync(ChatTransportWait? wait, CancellationToken cancellationToken);
}

public sealed record ChatTransportWait(DateTimeOffset RetryAt, int Attempt);
