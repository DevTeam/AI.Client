namespace AI.Infrastructure.Chat;

using AI.Application.Chat;

public sealed class ChatTransportActivity : IChatTransportActivity
{
    private readonly AsyncLocal<Scope?> _current = new();

    public IDisposable BeginScope(Func<ChatTransportWait?, CancellationToken, Task> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var scope = new Scope(this, _current.Value, report);
        _current.Value = scope;
        return scope;
    }

    public Task ReportAsync(ChatTransportWait? wait, CancellationToken cancellationToken) =>
        _current.Value?.Report(wait, cancellationToken) ?? Task.CompletedTask;

    private sealed class Scope(
        ChatTransportActivity owner,
        Scope? previous,
        Func<ChatTransportWait?, CancellationToken, Task> report) : IDisposable
    {
        public Task Report(ChatTransportWait? wait, CancellationToken token) => report(wait, token);

        public void Dispose()
        {
            if (ReferenceEquals(owner._current.Value, this)) owner._current.Value = previous;
        }
    }
}
