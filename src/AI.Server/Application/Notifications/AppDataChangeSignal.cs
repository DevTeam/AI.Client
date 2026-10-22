namespace AI.Application.Notifications;

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AI.Application.Users;
using AI.Domain.Users;

/// <inheritdoc />
/// <remarks>A change reaches only the subscribers of the user it was made for.</remarks>
public sealed class AppDataChangeSignal(IUserContext users) : IAppDataChangeSignal
{
    private readonly Lock _gate = new();
    private readonly List<(UserId User, Channel<long> Channel)> _subscribers = [];
    private long _version;

    public void Notify()
    {
        var user = users.Current;
        lock (_gate)
        {
            _version++;
            // Each subscriber holds a one-slot channel that drops the older value, so a burst of
            // mutations collapses into a single wake-up instead of a queue of identical ones.
            foreach (var subscriber in _subscribers.Where(item => item.User == user))
                subscriber.Channel.Writer.TryWrite(_version);
        }
    }

    /// <remarks>
    /// Registration happens when this is called, not when enumeration starts. An iterator would
    /// defer it, and a change landing in that gap would be lost — precisely the gap a subscriber
    /// opens between deciding to listen and getting around to it.
    /// </remarks>
    public IAsyncEnumerable<long> SubscribeAsync(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<long>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
        var subscriber = (User: users.Current, Channel: channel);
        lock (_gate) _subscribers.Add(subscriber);
        return ReadAsync(subscriber, cancellationToken);
    }

    private async IAsyncEnumerable<long> ReadAsync(
        (UserId User, Channel<long> Channel) subscriber, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var version in subscriber.Channel.Reader.ReadAllAsync(cancellationToken)) yield return version;
        }
        finally
        {
            lock (_gate) _subscribers.Remove(subscriber);
        }
    }
}
