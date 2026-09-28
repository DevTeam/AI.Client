namespace AI.Server.Hosting;

/// <summary>Looks where the Desktop installers of build/Packaging put the app.</summary>
public sealed class InstalledDesktop : IInstalledDesktop
{
    public bool IsInstalled
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "AI Client Desktop", "AI.Desktop.exe"));
            if (OperatingSystem.IsMacOS())
                return Directory.Exists("/Applications/AI Client.app");
            if (OperatingSystem.IsLinux())
                return File.Exists("/opt/ai-client-desktop/AI.Desktop");
            return false;
        }
    }
}
