namespace AI.Application.Notifications;

using AI.Contracts.Navigation;

/// <summary>
/// Asks the open windows to show a project, chat or branch. Unlike <see cref="IAppDataChangeSignal"/>
/// it carries its target, and only the latest request matters: a window that was away opens the
/// last place it was sent, not every place in between.
/// </summary>
public interface IAppNavigationSignal
{
    /// <summary>Records the request. Never blocks and never throws; with nobody listening it is lost.</summary>
    void Navigate(AppNavigation target);

    IAsyncEnumerable<AppNavigation> SubscribeAsync(CancellationToken cancellationToken);
}
