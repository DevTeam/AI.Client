namespace AI.Updates;

using AI.Contracts.FileSystem;

public sealed class UpdateManagerFactory(HttpClient http, IFileSystem files, IAtomicFileWriter atomic,
    IUpdateFeed feed, IUpdateInstaller installer, IUpdateInstallationProvider installation) : IUpdateManagerFactory
{
    public IUpdateManager Create(string product, string dataDirectory,
        Func<CancellationToken, Task<bool>> tryEnterMaintenance, Action leaveMaintenance, Action shutdown) =>
        new UpdateManager(product, dataDirectory, http, files, atomic, feed, installer,
            installation.Inspect(product), tryEnterMaintenance, leaveMaintenance, shutdown);
}
