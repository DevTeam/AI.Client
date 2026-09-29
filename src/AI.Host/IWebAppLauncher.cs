namespace AI.Host;

/// <summary>Opens the public Web app in the default browser, already paired with a running Host.</summary>
internal interface IWebAppLauncher
{
    Task OpenAsync(Uri hostAddress, CancellationToken cancellationToken);
}
