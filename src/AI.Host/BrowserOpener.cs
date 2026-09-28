namespace AI.Host;

using System.Diagnostics;

internal sealed class BrowserOpener : IBrowserOpener
{
    public void Open(string url)
    {
        // The shell hands the whole address, fragment included, to the registered browser.
        var start = OperatingSystem.IsWindows() ? new ProcessStartInfo(url) { UseShellExecute = true }
            : new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
        if (!start.UseShellExecute) start.ArgumentList.Add(url);
        using var _ = Process.Start(start);
    }
}
