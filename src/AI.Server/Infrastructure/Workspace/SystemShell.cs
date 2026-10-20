namespace AI.Infrastructure.Workspace;

using System.Diagnostics;
using AI.Application.Resources;

/// <summary>Opens paths with Explorer, <c>open</c> or <c>xdg-open</c>.</summary>
public sealed class SystemShell : ISystemShell
{
    // Opening one of these runs it: the user asked to see the file, not to launch it.
    private static readonly HashSet<string> Runnable = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
        ".msi", ".msp", ".scr", ".pif", ".lnk", ".url", ".reg", ".cpl", ".hta", ".jar", ".appref-ms",
        ".app", ".command", ".sh", ".desktop", ".run", ".appimage"
    };

    public void Open(string path, bool isDirectory)
    {
        var reveal = !isDirectory && Runnable.Contains(Path.GetExtension(path));
        ProcessStartInfo start;
        if (OperatingSystem.IsWindows())
        {
            // Explorer parses its own command line: "/select," must stay outside the quotes, and a
            // Windows path cannot contain a quote to break out of them.
            start = reveal ? new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                : isDirectory ? new ProcessStartInfo("explorer.exe", $"\"{path}\"")
                : new ProcessStartInfo(path) { UseShellExecute = true };
        }
        else if (OperatingSystem.IsMacOS())
        {
            start = new ProcessStartInfo("open") { UseShellExecute = false };
            if (reveal) start.ArgumentList.Add("-R");
            start.ArgumentList.Add(path);
        }
        else
        {
            // xdg-open has no "reveal": a runnable file opens its folder instead.
            start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            start.ArgumentList.Add(reveal ? Path.GetDirectoryName(path) ?? path : path);
        }
        using var _ = Process.Start(start);
    }
}
