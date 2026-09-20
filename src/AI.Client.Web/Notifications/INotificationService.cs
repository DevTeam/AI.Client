namespace AI.Client.Web.Notifications;

/// <summary>
/// App-wide transient notifications (toasts) shown in a single fixed region rendered by App.razor.
/// One at a time: a new notification replaces the current one rather than stacking, because the
/// operations that trigger toasts here (saving settings, deleting a chat, picking an endpoint) all
/// carry the same urgency, and stacking them in the corner was both visually noisy and prone to
/// covering the most recent (and most interesting) message with an older one. Auto-dismiss is the
/// same 4 seconds the previous in-place toast used, so an attentive user still sees the message
/// even if they don't look at the corner until later.
/// </summary>
public interface INotificationService
{
    /// <summary>The currently visible notification, or null if none is shown.</summary>
    NotificationMessage? Current { get; }

    /// <summary>Raised when the visible notification changes. Subscribers re-render in response.</summary>
    event Action? Changed;

    /// <summary>Shows a success notification (green border).</summary>
    void ShowSuccess(string message);

    /// <summary>Shows an error notification (red border). The message is meant for end users, not logs.</summary>
    void ShowError(string message);

    /// <summary>Shows an informational notification (blue border).</summary>
    void ShowInfo(string message);

    /// <summary>
    /// Removes the current notification immediately. Callers rarely need this — the auto-dismiss
    /// timer handles the normal case — but Escape in Home.razor still calls it so a quick keypress
    /// can clear the corner without waiting.
    /// </summary>
    void Dismiss();
}

/// <summary>
/// One notification on screen. Kept as a public record so consumers (ToastRegion.razor, tests)
/// can pattern-match on <see cref="Kind"/> without depending on the singleton service.
/// </summary>
/// <param name="Message">The text shown to the user.</param>
/// <param name="Kind">success, error or info — drives the border colour and the leading icon.</param>
public sealed record NotificationMessage(string Message, NotificationKind Kind);

public enum NotificationKind
{
    Success,
    Error,
    Info
}
