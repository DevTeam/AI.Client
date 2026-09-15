namespace AI.Client.Application.Notifications;

using System.Runtime.CompilerServices;
using System.Threading.Channels;

/// <inheritdoc />
public sealed class AppDataChangeSignal : IAppDataChangeSignal
{
    private readonly Lock _gate = new();
    private readonly List<Channel<long>> _subscribers = [];
    private long _version;

    public void Notify()
    {
        lock (_gate)
        {
            _version++;
            // Each subscriber holds a one-slot channel that drops the older value, so a burst of
            // mutations collapses into a single wake-up instead of a queue of identical ones.
            foreach (var subscriber in _subscribers) subscriber.Writer.TryWrite(_version);
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
        lock (_gate) _subscribers.Add(channel);
        return ReadAsync(channel, cancellationToken);
    }

    private async IAsyncEnumerable<long> ReadAsync(
        Channel<long> channel, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var version in channel.Reader.ReadAllAsync(cancellationToken)) yield return version;
        }
        finally
        {
            lock (_gate) _subscribers.Remove(channel);
        }
    }
}
