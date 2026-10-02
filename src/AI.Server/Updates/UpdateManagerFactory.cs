namespace AI.Updates;

public sealed class UpdateManagerFactory(HttpClient http, IUpdateFeed feed, IUpdateInstaller installer,
    IUpdateInstallationProvider installation) : IUpdateManagerFactory
{
    public IUpdateManager Create(string product, string dataDirectory,
        Func<CancellationToken, Task<bool>> tryEnterMaintenance, Action leaveMaintenance, Action shutdown) =>
        new UpdateManager(product, dataDirectory, http, feed, installer, installation.Inspect(product), tryEnterMaintenance, leaveMaintenance, shutdown);
}
