namespace AI.Web.State;

/// <summary>
/// Builds <see cref="IDelayedBusyIndicator"/> instances. Each owner wants its own — the indicator
/// carries timers and a per-instance state, and two sidebars sharing one would blink in lockstep.
/// </summary>
/// <remarks>
/// The notifyChanged callback is owner-specific (it has to re-render the component that owns
/// the indicator), so it cannot come from DI and is passed at construction time instead. That is
/// the only reason this factory exists: it is the seam between DI-resolvable lifetimes and the
/// one piece of state that the container has no business knowing about.
/// </remarks>
public interface IDelayedBusyIndicatorFactory
{
    /// <param name="notifyChanged">Re-renders the owner: <see cref="IDelayedBusyIndicator.IsVisible"/> changes on a timer, off the call that started the load.</param>
    /// <param name="showDelay">How long a load may run before it gets a placeholder at all.</param>
    /// <param name="minimumVisible">How long the placeholder stays once it has been shown.</param>
    IDelayedBusyIndicator Create(Func<Task> notifyChanged, TimeSpan? showDelay = null, TimeSpan? minimumVisible = null);
}
