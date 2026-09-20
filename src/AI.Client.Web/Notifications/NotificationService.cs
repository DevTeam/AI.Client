namespace AI.Client.Web.Notifications;

/// <summary>
/// Single-instance holder of the currently visible notification. Lives for the lifetime of the
/// Blazor app: <see cref="Changed"/> subscribers re-render, <see cref="Dismiss"/> clears. The
/// auto-dismiss timer is cancelled on every replacement so a constant stream of toasts (a long
/// save loop, repeated queue commands) never strands a half-dismissed one over a fresher one.
/// </summary>
public sealed class NotificationService : INotificationService
{
    // Same four seconds Home.razor's old in-place toast used. Tuned for a glance, not for reading —
    // the operations that trigger these toasts are confirmations ("Project saved", "Chat deleted"),
    // not explanations, and a longer dwell time would only make the corner feel stale on screen.
    private static readonly TimeSpan AutoDismissAfter = TimeSpan.FromSeconds(4);

    private CancellationTokenSource? _autoDismissCts;

    public NotificationMessage? Current { get; private set; }

    public event Action? Changed;

    public void ShowSuccess(string message) => Show(new NotificationMessage(message, NotificationKind.Success));

    public void ShowError(string message) => Show(new NotificationMessage(message, NotificationKind.Error));

    public void ShowInfo(string message) => Show(new NotificationMessage(message, NotificationKind.Info));

    public void Dismiss()
    {
        if (Current is null) return;
        CancelAutoDismiss();
        Current = null;
        Changed?.Invoke();
    }

    private void Show(NotificationMessage message)
    {
        // Replacing the previous message always cancels its dismiss timer: otherwise a constant
        // stream of toasts (every save, every queue command) would leave the new message auto-clearing
        // by the OLD timer, popping the corner mid-glance. The new timer is owned by the new message.
        CancelAutoDismiss();
        Current = message;
        Changed?.Invoke();

        var cts = new CancellationTokenSource();
        _autoDismissCts = cts;
        _ = AutoDismissAsync(cts);
    }

    private async Task AutoDismissAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(AutoDismissAfter, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Only clear if this timer is still the active one. A Show() that arrived during the delay
        // swapped _autoDismissCts to a newer instance; clearing Current now would wipe that newer
        // message and pop the corner mid-glance.
        if (!ReferenceEquals(_autoDismissCts, cts)) return;
        _autoDismissCts = null;
        Current = null;
        Changed?.Invoke();
    }

    private void CancelAutoDismiss()
    {
        _autoDismissCts?.Cancel();
        _autoDismissCts?.Dispose();
        _autoDismissCts = null;
    }
}
