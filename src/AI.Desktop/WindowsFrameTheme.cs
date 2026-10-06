namespace AI.Desktop;

using System.Runtime.InteropServices;

/// <summary>
/// Avalonia darkens the system titlebar only on Windows 11, so Windows 10 kept a white titlebar
/// over a dark workspace. The attribute has been honoured since Windows 10 2004 (build 19041);
/// older builds and other systems ignore the call.
/// </summary>
internal sealed partial class WindowsFrameTheme : IWindowFrameTheme
{
    private const int UseImmersiveDarkMode = 20;
    private const uint NoSize = 0x1, NoMove = 0x2, NoZOrder = 0x4, NoActivate = 0x10, FrameChanged = 0x20;

    public void Apply(IntPtr windowHandle, bool dark)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) || windowHandle == IntPtr.Zero) return;
        var value = dark ? 1 : 0;
        if (DwmSetWindowAttribute(windowHandle, UseImmersiveDarkMode, ref value, sizeof(int)) < 0) return;
        // Windows 10 repaints the non-client area only when told the frame changed.
        SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, 0, 0, NoSize | NoMove | NoZOrder | NoActivate | FrameChanged);
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
