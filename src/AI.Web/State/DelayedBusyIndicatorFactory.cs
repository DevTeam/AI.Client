namespace AI.Web.State;

/// <summary>
/// Default factory: hands out fresh <see cref="DelayedBusyIndicator"/> instances so two consumers
/// never share a timer.
/// </summary>
public sealed class DelayedBusyIndicatorFactory : IDelayedBusyIndicatorFactory
{
    public IDelayedBusyIndicator Create(Func<Task> notifyChanged, TimeSpan? showDelay = null, TimeSpan? minimumVisible = null) =>
        new DelayedBusyIndicator(notifyChanged, showDelay, minimumVisible);
}
