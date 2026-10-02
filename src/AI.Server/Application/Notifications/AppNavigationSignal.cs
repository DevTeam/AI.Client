namespace AI.Application.Notifications;

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AI.Contracts.Navigation;

/// <inheritdoc />
public sealed class AppNavigationSignal : IAppNavigationSignal
{
    private readonly Lock _gate = new();
    private readonly List<Channel<AppNavigation>> _subscribers = [];
    private readonly Dictionary<Guid, Pending> _pending = [];

    private sealed class Pending(DateTimeOffset expiresAt)
    {
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public Guid? ClientId { get; set; }
        public TaskCompletionSource<AppNavigationResponse> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public async Task<AppNavigationResponse> RequestAsync(AppNavigation target, CancellationToken cancellationToken)
    {
        var expiresAt = target.ExpiresAt ?? DateTimeOffset.UtcNow.AddSeconds(AppNavigation.DefaultTimeoutSeconds);
        if (expiresAt <= DateTimeOffset.UtcNow) return new("expired", "This action has expired.");
        var pending = new Pending(expiresAt);
        target = target with { RequestId = Guid.CreateVersion7(), ExpiresAt = expiresAt };
        lock (_gate)
        {
            if (_subscribers.Count == 0) return new("unavailable", "No application window is connected.");
            _pending.Add(target.RequestId, pending);
            foreach (var subscriber in _subscribers) subscriber.Writer.TryWrite(target);
        }
        try
        {
            var remaining = expiresAt - DateTimeOffset.UtcNow;
            return await pending.Completion.Task.WaitAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, cancellationToken);
        }
        catch (TimeoutException) { return new("expired", "The user did not continue this action in time."); }
        finally { lock (_gate) _pending.Remove(target.RequestId); }
    }

    public bool Claim(Guid requestId, Guid clientId)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(requestId, out var pending) || pending.ExpiresAt <= DateTimeOffset.UtcNow || clientId == Guid.Empty) return false;
            if (pending.ClientId is { } owner && owner != clientId) return false;
            pending.ClientId = clientId;
            return true;
        }
    }

    public bool Complete(Guid requestId, AppNavigationDecision decision)
    {
        lock (_gate)
            return _pending.TryGetValue(requestId, out var pending) && pending.ClientId == decision.ClientId
                && pending.ExpiresAt > DateTimeOffset.UtcNow
                && decision.Outcome is "applied" or "stopped" or "unavailable" or "expired"
                && pending.Completion.TrySetResult(new(decision.Outcome, decision.Error, decision.Targets));
    }

    public void Navigate(AppNavigation target)
    {
        lock (_gate)
            foreach (var subscriber in _subscribers) subscriber.Writer.TryWrite(target);
    }

    /// <remarks>Registers at the call, not at the first enumeration, as <see cref="AppDataChangeSignal"/> does.</remarks>
    public IAsyncEnumerable<AppNavigation> SubscribeAsync(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<AppNavigation>(new UnboundedChannelOptions { SingleReader = true });
        lock (_gate) _subscribers.Add(channel);
        return ReadAsync(channel, cancellationToken);
    }

    private async IAsyncEnumerable<AppNavigation> ReadAsync(
        Channel<AppNavigation> channel, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var target in channel.Reader.ReadAllAsync(cancellationToken)) yield return target;
        }
        finally
        {
            lock (_gate) _subscribers.Remove(channel);
        }
    }
}
