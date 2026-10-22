namespace AI.Application.Notifications;

using AI.Contracts.Navigation;

/// <summary>
/// Asks the open windows to show a project, chat or branch. Unlike <see cref="IAppDataChangeSignal"/>
/// it carries its target, and only the latest request matters: a window that was away opens the
/// last place it was sent, not every place in between.
/// </summary>
public interface IAppNavigationSignal
{
    /// <summary>
    /// Records the request for the current user's windows. Never blocks; with nobody listening it is
    /// lost. Throws only when no user scope is active, which is a defect of the caller.
    /// </summary>
    void Navigate(AppNavigation target);

    Task<AppNavigationResponse> RequestAsync(AppNavigation target, CancellationToken cancellationToken);
    bool Claim(Guid requestId, Guid clientId);
    bool Complete(Guid requestId, AppNavigationDecision decision);

    IAsyncEnumerable<AppNavigation> SubscribeAsync(CancellationToken cancellationToken);
}
