namespace AI.Application.Plans;

using System.Collections.Concurrent;
using Chats;
using Contracts.Plans;
using Domain.Chats;
using Domain.Projects;
using Projects;
using Tools;

/// <summary>
/// The plan of each running branch. The history is the plan's record, and this is the run's working
/// copy of it: read from the branch once when the run begins, then changed by the plan tool and read
/// before every model step, which would otherwise load the whole chat each time.
/// </summary>
/// <remarks>
/// It also keeps the plan's working clock (<see cref="ChatPlan.Clock"/>) for the run: the clock goes on
/// from where the plan left it when the run begins, so the time between turns is not counted, and it
/// stands still while the run waits for the person.
/// </remarks>
public interface IChatPlans
{
    Task<IDisposable> BeginAsync(ToolRunContext run, CancellationToken cancellationToken);

    /// <summary>The branch's plan; null when it has none, or when no run of it is in progress.</summary>
    ChatPlan? Current(ToolRunContext run);

    /// <summary>Now, by the wall clock and by the plan's working clock.</summary>
    ChatPlanMoment Now(ToolRunContext run);

    /// <summary>Stops the working clock until the returned scope ends: the run is waiting for the person.</summary>
    IDisposable WaitForPerson(ToolRunContext run);

    /// <summary>Records a tool call made on the branch, so a criterion can cite it as evidence.</summary>
    void NoteCall(ToolRunContext run, string callId);

    /// <summary>Whether the branch has a tool call by this id: this run's, or one in its history.</summary>
    bool KnowsCall(ToolRunContext run, string callId);

    /// <summary>False when the run has ended, or the plan was changed since <paramref name="expected"/> was read.</summary>
    bool TrySet(ToolRunContext run, ChatPlan? expected, ChatPlan plan);
}

public sealed class ChatPlans(IChatRepository chats, IClock clock) : IChatPlans
{
    private readonly ConcurrentDictionary<Key, Entry> _entries = new();

    public async Task<IDisposable> BeginAsync(ToolRunContext run, CancellationToken cancellationToken)
    {
        var key = Key.Of(run);
        var (plan, calls) = await LoadAsync(run, cancellationToken);
        var entry = new Entry(plan, plan?.Clock ?? 0, clock.UtcNow);
        foreach (var call in calls) entry.Calls.Add(call);
        _entries[key] = entry;
        return new Scope(() => _entries.TryRemove(new KeyValuePair<Key, Entry>(key, entry)));
    }

    public ChatPlan? Current(ToolRunContext run) => _entries.TryGetValue(Key.Of(run), out var entry) ? entry.Plan : null;

    public ChatPlanMoment Now(ToolRunContext run)
    {
        var now = clock.UtcNow;
        if (!_entries.TryGetValue(Key.Of(run), out var entry)) return new ChatPlanMoment(now, 0);
        lock (entry) return new ChatPlanMoment(now, entry.ClockAt(now));
    }

    public void NoteCall(ToolRunContext run, string callId)
    {
        if (_entries.TryGetValue(Key.Of(run), out var entry)) lock (entry) entry.Calls.Add(callId);
    }

    public bool KnowsCall(ToolRunContext run, string callId)
    {
        if (!_entries.TryGetValue(Key.Of(run), out var entry)) return false;
        lock (entry) return entry.Calls.Contains(callId);
    }

    public IDisposable WaitForPerson(ToolRunContext run)
    {
        if (!_entries.TryGetValue(Key.Of(run), out var entry)) return new Scope(() => { });
        lock (entry)
        {
            // Questions and approvals of one batch can overlap; the clock stops at the first and goes
            // on after the last.
            if (entry.Waits++ == 0) entry.WaitingSince = clock.UtcNow;
        }
        return new Scope(() =>
        {
            lock (entry)
            {
                if (--entry.Waits > 0) return;
                var now = clock.UtcNow;
                entry.Waited += now - (entry.WaitingSince ?? now);
                entry.WaitingSince = null;
            }
        });
    }

    public bool TrySet(ToolRunContext run, ChatPlan? expected, ChatPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!_entries.TryGetValue(Key.Of(run), out var entry)) return false;
        lock (entry)
        {
            if (!ReferenceEquals(entry.Plan, expected)) return false;
            entry.Plan = plan;
            // The plan carries the clock from here on; a wait in progress counts from here.
            entry.Base = plan.Clock;
            entry.Since = plan.UpdatedAt;
            entry.Waited = TimeSpan.Zero;
            if (entry.WaitingSince is not null) entry.WaitingSince = plan.UpdatedAt;
            return true;
        }
    }

    /// <summary>
    /// A chat that cannot be read leaves the run without its earlier plan rather than failing it: the
    /// model can lay the plan out again.
    /// </summary>
    private async Task<(ChatPlan? Plan, IReadOnlyList<string> Calls)> LoadAsync(ToolRunContext run, CancellationToken cancellationToken)
    {
        try
        {
            var stored = await chats.GetAsync(new ProjectId(run.ProjectId), new ChatId(run.ChatId), cancellationToken);
            var head = stored?.Chat.Branches.SingleOrDefault(branch => branch.Id == run.BranchId)?.HeadMessageId;
            if (head is null) return (null, []);
            var branch = stored!.Chat.GetBranch(head);
            return (ChatPlanHistory.Latest(branch),
                branch.SelectMany(message => message.ToolCalls ?? []).Select(call => call.Id).ToArray());
        }
        catch (Exception error) when (error is AI.Domain.Common.DomainException or IOException
                                          or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return (null, []);
        }
    }

    private readonly record struct Key(Guid ProjectId, Guid ChatId, Guid BranchId)
    {
        public static Key Of(ToolRunContext run) => new(run.ProjectId, run.ChatId, run.BranchId);
    }

    /// <summary>The run's copy of the plan, and its working clock: <see cref="Base"/> seconds at <see cref="Since"/>, less the waits since.</summary>
    private sealed class Entry(ChatPlan? plan, double clock, DateTimeOffset since)
    {
        public ChatPlan? Plan { get; set; } = plan;
        public double Base { get; set; } = clock;
        public DateTimeOffset Since { get; set; } = since;
        public TimeSpan Waited { get; set; }
        public DateTimeOffset? WaitingSince { get; set; }
        public int Waits { get; set; }
        public HashSet<string> Calls { get; } = new(StringComparer.Ordinal);

        public double ClockAt(DateTimeOffset now) =>
            Base + Math.Max(0, (now - Since - Waited - (WaitingSince is { } waiting ? now - waiting : TimeSpan.Zero)).TotalSeconds);
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
