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

    private readonly Dictionary<(Guid ChatId, Guid BranchId), DateTimeOffset> _watched = [];
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
        var seen = new HashSet<(Guid ChatId, Guid BranchId)>();
        foreach (var project in await projects.ListAsync(token))
        {
        var summaries = (await chats.ListAsync(project.Id, token)).ToDictionary(chat => chat.Id);
        foreach (var owner in await store.ListAsync(project.Id, token))
        {
            var key = (owner.ChatId, owner.BranchId);
            seen.Add(key);
            try
            {
                summaries.TryGetValue(owner.ChatId, out var summary);
                if (summary is null || summary.ArchivedAt is not null) continue;
                var due = pass.DueAt(owner.Schedule, now, _watched.TryGetValue(key, out var watched) ? watched : null);
                if (due <= now)
                {
                    await pass.ProcessAsync(project.Id, owner.ChatId, owner.BranchId, now, token);
                    _watched[key] = now;
                    var after = await store.ReadAsync(project.Id, owner.ChatId, owner.BranchId, token);
                    if (after?.Schedule is { } changed)
                    {
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
                PassFailed(logger, owner.ChatId, error);
            }
        }
        }
        foreach (var gone in _watched.Keys.Where(id => !seen.Contains(id)).ToArray()) _watched.Remove(gone);
        var delay = wake - clock.UtcNow;
        return delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay > Heartbeat ? Heartbeat : delay;
    }
}
