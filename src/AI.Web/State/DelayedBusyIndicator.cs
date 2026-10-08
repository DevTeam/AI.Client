namespace AI.Web.State;

/// <summary>
/// A busy flag that lags on purpose. It stays down for the first <c>showDelay</c> of a load and,
/// once raised, stays up for at least <c>minimumVisible</c>.
/// </summary>
/// <remarks>
/// The gateway is local and usually answers in tens of milliseconds, so an honest flag would flash
/// a placeholder for two frames — read as a rendering glitch rather than as feedback — while a
/// load that only just crosses the threshold would blink out again exactly as the eye arrives.
/// Both ends of the window are therefore damped: nothing at all for loads nobody waits on, and a
/// stable placeholder for the loads they do.
/// </remarks>
public sealed class DelayedBusyIndicator : IDelayedBusyIndicator
{
    private readonly IBusyIndicatorTime _time;
    private readonly Func<Task> _notifyChanged;
    private readonly TimeSpan _showDelay;
    private readonly TimeSpan _minimumVisible;
    private bool _isLoading;
    private bool _showScheduled;
    private bool _hideScheduled;
    private bool _disposed;
    private long _shownAt;

    /// <param name="notifyChanged">Re-renders the owner: <see cref="IsVisible"/> changes on a timer, off the call that started the load.</param>
    /// <param name="showDelay">How long a load may run before it gets a placeholder at all.</param>
    /// <param name="minimumVisible">How long the placeholder stays once it has been shown.</param>
    public DelayedBusyIndicator(IBusyIndicatorTime time, Func<Task> notifyChanged,
        TimeSpan? showDelay = null, TimeSpan? minimumVisible = null)
    {
        _time = time;
        _notifyChanged = notifyChanged;
        _showDelay = showDelay ?? TimeSpan.FromMilliseconds(150);
        _minimumVisible = minimumVisible ?? TimeSpan.FromMilliseconds(300);
    }

    /// <summary>Whether the owner should render its placeholder right now.</summary>
    public bool IsVisible { get; private set; }

    /// <summary>
    /// Marks the start of a load. Call it only when there is nothing on screen worth keeping: a
    /// background refresh of a list the user is already reading must not replace it with bones.
    /// </summary>
    public void Begin()
    {
        _isLoading = true;
        // Already up, or already counting down towards being up — a second load joins the first
        // rather than restarting its clock, so back-to-back loads don't stutter the placeholder.
        if (IsVisible || _showScheduled) return;

        _showScheduled = true;
        _ = ShowAfterDelayAsync();
    }

    /// <summary>
    /// Marks the end of a load, and returns immediately: the placeholder may outlive the call by
    /// up to its minimum-visible time. Idempotent, so every path out of a load — including the
    /// early returns and the failing ones — can call it without tracking whether one was begun.
    /// </summary>
    public void End()
    {
        _isLoading = false;
        if (!IsVisible || _hideScheduled) return;

        _hideScheduled = true;
        _ = HideAfterMinimumAsync();
    }

    public void Dispose() => _disposed = true;

    private async Task ShowAfterDelayAsync()
    {
        await _time.DelayAsync(_showDelay);
        _showScheduled = false;
        // Finished inside the delay window: the placeholder never existed and never will.
        if (_disposed || !_isLoading || IsVisible) return;

        _shownAt = _time.GetTimestamp();
        IsVisible = true;
        await _notifyChanged();
    }

    private async Task HideAfterMinimumAsync()
    {
        var visibleFor = _time.GetElapsedTime(_shownAt);
        if (visibleFor < _minimumVisible) await _time.DelayAsync(_minimumVisible - visibleFor);
        _hideScheduled = false;
        // A new load claimed the placeholder while we were holding the floor; it keeps it, and
        // its own End schedules the next hide from the time this one was first shown.
        if (_disposed || _isLoading) return;

        IsVisible = false;
        await _notifyChanged();
    }
}
