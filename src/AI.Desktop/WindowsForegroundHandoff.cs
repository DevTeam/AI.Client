namespace AI.Desktop;

using System.Diagnostics;
using System.Runtime.InteropServices;
using AI.Infrastructure.Instances;

/// <summary>
/// Windows lets only the process the user just started take the foreground, so a second launch
/// grants it to the running instance before asking it to come to the front. Without this the
/// running window would only flash on the taskbar. Other systems need nothing.
/// </summary>
internal sealed partial class WindowsForegroundHandoff : IForegroundHandoff
{
    public void AllowForeground(int processId)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!AllowSetForegroundWindow(processId))
            Trace.TraceWarning($"Could not let process {processId} take the foreground: {Marshal.GetLastPInvokeError()}");
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
