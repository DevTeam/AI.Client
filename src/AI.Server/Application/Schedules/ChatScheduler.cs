namespace AI.Application.Schedules;

using AI.Application.Chats;
using AI.Application.Projects;
using AI.Contracts.Chats;
using AI.Contracts.Schedules;
using Microsoft.Extensions.Logging;

/// <summary>
/// The Host's dispatcher of scheduled chats: a loop that wakes when an occurrence, a retry, a
/// branch deletion or a run it watches is due, and otherwise at least every
/// <see cref="ChatScheduler.Heartbeat"/>.
/// </summary>
public interface IChatScheduler
{
    void Start();
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>Wakes the loop now: a schedule changed or a run reported.</summary>
    void Nudge();
}

public sealed class ChatScheduler(
    IProjectService projects,
    IChatService chats,
    IChatScheduleStore store,
    IScheduledChatPass pass,
    IClock clock,
    ILogger<ChatScheduler> logger) : IChatScheduler, IDisposable
{
    internal static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(30);

    private static readonly Action<ILogger, Guid, Exception?> PassFailed =
        LoggerMessage.Define<Guid>(LogLevel.Error, new EventId(1, "ScheduledChatPassFailed"), "Scheduled chat {ChatId} pass failed.");

    private static readonly Action<ILogger, Exception?> Started =
        LoggerMessage.Define(LogLevel.Information, new EventId(2, "ChatSchedulerStarted"), "The dispatcher of scheduled chats started.");

    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _stop = new();

    // The schedules last read, by chat and the chat revision they were read at. A chat that has not
    // changed is not read again: chat documents carry their whole history.
    private readonly Dictionary<Guid, (long Revision, ChatSchedule? Schedule)> _known = [];

    // When each chat was last processed, so a run that is going is looked at every few seconds
    // rather than on every pass.
    private readonly Dictionary<Guid, DateTimeOffset> _watched = [];
    private Task _loop = Task.CompletedTask;

    public void Start()
    {
        if (!_loop.IsCompleted) return;
        _loop = Task.Run(() => LoopAsync(_stop.Token), CancellationToken.None);
        Started(logger, null);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stop.CancelAsync();
        try { await _loop.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { }
    }

    public void Nudge()
    {
        if (_wake.CurrentCount == 0)
        {
            try { _wake.Release(); }
            catch (SemaphoreFullException) { }
        }
    }

    public void Dispose()
    {
        _stop.Dispose();
        _wake.Dispose();
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                delay = await PassAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            // The loop outlives any one failure: a dispatcher that stopped silently would leave every
            // schedule in the application without runs until the next restart.
            catch (Exception error) when (error is not OperationCanceledException)
            {
                PassFailed(logger, Guid.Empty, error);
                delay = Heartbeat;
            }
            try { await _wake.WaitAsync(delay, token); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One pass over every scheduled chat; returns how long to sleep before the next.</summary>
    private async Task<TimeSpan> PassAsync(CancellationToken token)
    {
        var now = clock.UtcNow;
        var wake = now + Heartbeat;
        var seen = new HashSet<Guid>();
        foreach (var project in await projects.ListAsync(token))
        foreach (var summary in await chats.ListAsync(project.Id, token))
        {
            if (summary.Kind != ChatSchedule.Kind || summary.ArchivedAt is not null) continue;
            seen.Add(summary.Id);
            try
            {
                if (!_known.TryGetValue(summary.Id, out var known) || known.Revision != summary.Revision)
                {
                    var read = await store.ReadAsync(project.Id, summary.Id, token);
                    known = (read?.ChatRevision ?? summary.Revision, read?.Schedule);
                    _known[summary.Id] = known;
                }
                if (known.Schedule is not { } schedule) continue;
                var due = pass.DueAt(schedule, now, _watched.TryGetValue(summary.Id, out var watched) ? watched : null);
                if (due <= now)
                {
                    await pass.ProcessAsync(project.Id, summary.Id, now, token);
                    _watched[summary.Id] = now;
                    // Read again on the next pass: processing changed the chat.
                    _known.Remove(summary.Id);
                    var after = await store.ReadAsync(project.Id, summary.Id, token);
                    if (after?.Schedule is { } changed)
                    {
                        _known[summary.Id] = (after.ChatRevision, changed);
                        due = pass.DueAt(changed, now, now);
                    }
                    else due = null;
                }
                if (due is { } at && at < wake) wake = at;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // One broken chat must not keep the others from running.
                _known.Remove(summary.Id);
                PassFailed(logger, summary.Id, error);
            }
        }
        foreach (var gone in _known.Keys.Where(id => !seen.Contains(id)).ToArray()) _known.Remove(gone);
        foreach (var gone in _watched.Keys.Where(id => !seen.Contains(id)).ToArray()) _watched.Remove(gone);
        var delay = wake - clock.UtcNow;
        return delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay > Heartbeat ? Heartbeat : delay;
    }
}
