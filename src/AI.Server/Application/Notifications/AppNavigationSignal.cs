namespace AI.Application.Notifications;

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AI.Contracts.Navigation;

/// <inheritdoc />
public sealed class AppNavigationSignal : IAppNavigationSignal
{
    private readonly Lock _gate = new();
    private readonly List<Channel<AppNavigation>> _subscribers = [];

    public void Navigate(AppNavigation target)
    {
        lock (_gate)
            foreach (var subscriber in _subscribers) subscriber.Writer.TryWrite(target);
    }

    /// <remarks>Registers at the call, not at the first enumeration, as <see cref="AppDataChangeSignal"/> does.</remarks>
    public IAsyncEnumerable<AppNavigation> SubscribeAsync(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<AppNavigation>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
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
