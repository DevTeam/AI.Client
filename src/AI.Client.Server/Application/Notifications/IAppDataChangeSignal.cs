namespace AI.Client.Application.Notifications;

/// <summary>
/// Announces that application data changed outside the client that is looking at it — today that
/// means a tool call, tomorrow anything else the Host does on its own.
/// </summary>
/// <remarks>
/// The signal deliberately carries no payload. A client cannot tell which project, chat or setting
/// moved, so it re-reads whatever it is currently showing. That costs a few extra requests and
/// saves having to keep an event schema in step with every resource's revisions — a trade the
/// local, single-user Host can afford.
/// </remarks>
public interface IAppDataChangeSignal
{
    /// <summary>Records a change. Never blocks and never throws; a signal nobody listens to is lost on purpose.</summary>
    void Notify();

    /// <summary>
    /// Yields once per change, coalescing bursts: a subscriber that was busy sees one signal, not
    /// the ten that arrived while it was away. The value is the change counter at the time of
    /// delivery and exists for diagnostics only.
    /// </summary>
    IAsyncEnumerable<long> SubscribeAsync(CancellationToken cancellationToken);
}
