namespace AI.Web.Components;

using AI.Contracts.Plans;

/// <summary>
/// The plan's working clock (<see cref="ChatPlan.Clock"/>) between two plan updates, as the page can
/// tell it: it goes on from the plan's last update, or from the start of the turn for a plan an
/// earlier turn left, and stands still while the run waits for the person. The next update brings
/// the Host's own reading, which also leaves out waits that ended while the page was not watching.
/// </summary>
public interface IChatPlanClock
{
    /// <param name="workingSince">When the current run began, for a plan an earlier turn left; null when no run is working on it.</param>
    /// <param name="waiting">Whether the run is waiting for the person now.</param>
    double Now(ChatPlan plan, DateTimeOffset? workingSince, bool waiting);
}

/// <summary>Remembers the waits it has seen since the plan's last update; one open plan at a time.</summary>
public sealed class ChatPlanClock(TimeProvider time) : IChatPlanClock
{
    private (DateTimeOffset UpdatedAt, double Clock)? _plan;
    private TimeSpan _waited;
    private DateTimeOffset? _waitingSince;

    public double Now(ChatPlan plan, DateTimeOffset? workingSince, bool waiting)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (workingSince is not { } since) return plan.Clock;
        var now = time.GetUtcNow();
        if (_plan != (plan.UpdatedAt, plan.Clock))
        {
            _plan = (plan.UpdatedAt, plan.Clock);
            _waited = TimeSpan.Zero;
            _waitingSince = null;
        }
        if (waiting) _waitingSince ??= now;
        else if (_waitingSince is { } began)
        {
            _waited += now - began;
            _waitingSince = null;
        }
        var from = plan.UpdatedAt > since ? plan.UpdatedAt : since;
        var worked = now - from - _waited - (_waitingSince is { } paused ? now - paused : TimeSpan.Zero);
        return plan.Clock + Math.Max(0, worked.TotalSeconds);
    }
}
