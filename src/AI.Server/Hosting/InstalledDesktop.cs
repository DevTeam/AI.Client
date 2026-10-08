namespace AI.Server.Hosting;

using AI.Contracts.FileSystem;

/// <summary>Looks where the Desktop installers of build/Packaging put the app.</summary>
public sealed class InstalledDesktop(IFileSystem files) : IInstalledDesktop
{
    public bool IsInstalled
    {
        get
        {
            // An executable's own installation layout is checked before anything is served, so the
            // contract is awaited here instead of making this probe asynchronous.
            if (OperatingSystem.IsWindows())
                return Wait(files.FileExistsAsync(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "AI Client Desktop", "AI.Desktop.exe"), CancellationToken.None));
            if (OperatingSystem.IsMacOS())
                return Wait(files.DirectoryExistsAsync("/Applications/AI Client.app", CancellationToken.None));
            if (OperatingSystem.IsLinux())
                return Wait(files.FileExistsAsync("/opt/ai-client-desktop/AI.Desktop", CancellationToken.None));
            return false;
        }
    }

    private static bool Wait(Task<bool> operation) => operation.GetAwaiter().GetResult();
}
