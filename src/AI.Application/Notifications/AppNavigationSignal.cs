namespace AI.Application.Notifications;

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AI.Application.Users;
using AI.Contracts.Navigation;
using AI.Domain.Users;

/// <inheritdoc />
/// <remarks>
/// A request reaches only the windows of the user it was made for, and only those windows can claim
/// and answer it.
/// </remarks>
public sealed class AppNavigationSignal(IUserContext users) : IAppNavigationSignal
{
    private readonly Lock _gate = new();
    private readonly List<(UserId User, Channel<AppNavigation> Channel)> _subscribers = [];
    private readonly Dictionary<Guid, Pending> _pending = [];

    private sealed class Pending(UserId user, DateTimeOffset expiresAt)
    {
        public UserId User { get; } = user;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public Guid? ClientId { get; set; }
        public TaskCompletionSource<AppNavigationResponse> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public async Task<AppNavigationResponse> RequestAsync(AppNavigation target, CancellationToken cancellationToken)
    {
        var expiresAt = target.ExpiresAt ?? DateTimeOffset.UtcNow.AddSeconds(AppNavigation.DefaultTimeoutSeconds);
        if (expiresAt <= DateTimeOffset.UtcNow) return new("expired", "This action has expired.");
        var user = users.Current;
        var pending = new Pending(user, expiresAt);
        target = target with { RequestId = Guid.CreateVersion7(), ExpiresAt = expiresAt };
        lock (_gate)
        {
            var windows = _subscribers.Where(item => item.User == user).ToArray();
            if (windows.Length == 0) return new("unavailable", "No application window is connected.");
            _pending.Add(target.RequestId, pending);
            foreach (var window in windows) window.Channel.Writer.TryWrite(target);
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
        var user = users.Current;
        lock (_gate)
        {
            if (!_pending.TryGetValue(requestId, out var pending) || pending.User != user
                || pending.ExpiresAt <= DateTimeOffset.UtcNow || clientId == Guid.Empty) return false;
            if (pending.ClientId is { } owner && owner != clientId) return false;
            pending.ClientId = clientId;
            return true;
        }
    }

    public bool Complete(Guid requestId, AppNavigationDecision decision)
    {
        var user = users.Current;
        lock (_gate)
            return _pending.TryGetValue(requestId, out var pending) && pending.User == user
                && pending.ClientId == decision.ClientId
                && pending.ExpiresAt > DateTimeOffset.UtcNow
                && decision.Outcome is "applied" or "stopped" or "unavailable" or "expired"
                && pending.Completion.TrySetResult(new(decision.Outcome, decision.Error, decision.Targets, decision.Theme));
    }

    public void Navigate(AppNavigation target)
    {
        var user = users.Current;
        lock (_gate)
            foreach (var window in _subscribers.Where(item => item.User == user)) window.Channel.Writer.TryWrite(target);
    }

    /// <remarks>Registers at the call, not at the first enumeration, as <see cref="AppDataChangeSignal"/> does.</remarks>
    public IAsyncEnumerable<AppNavigation> SubscribeAsync(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<AppNavigation>(new UnboundedChannelOptions { SingleReader = true });
        var subscriber = (User: users.Current, Channel: channel);
        lock (_gate) _subscribers.Add(subscriber);
        return ReadAsync(subscriber, cancellationToken);
    }

    private async IAsyncEnumerable<AppNavigation> ReadAsync(
        (UserId User, Channel<AppNavigation> Channel) subscriber, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var target in subscriber.Channel.Reader.ReadAllAsync(cancellationToken)) yield return target;
        }
        finally
        {
            lock (_gate) _subscribers.Remove(subscriber);
        }
    }
}
