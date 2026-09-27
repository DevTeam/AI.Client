namespace AI.Desktop;

using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>Windows-only taskbar overlay; all native entry points stay behind the OS guard.</summary>
internal sealed partial class WindowsTaskbarBadge : ITaskbarBadge
{
    private static readonly Guid TaskbarClass = new("56FDF344-FD6D-11D0-958A-006097C9A090");
    private static readonly Guid TaskbarInterface = new("C43DC798-95D1-4BEA-9030-BB99E2983A1A");
    private const uint InProcessServer = 1;
    private const uint LoadFromFile = 0x10;
    private const uint ImageIcon = 1;
    // COM vtables begin with QueryInterface, AddRef and Release. ITaskbarList3 then
    // adds SetOverlayIcon after 5 ITaskbarList, 1 ITaskbarList2 and 9 earlier methods.
    private const int HrInitSlot = 3;
    private const int SetOverlayIconSlot = 18;

    private IntPtr _windowHandle;
    private IntPtr _taskbar;
    private SetOverlayIconDelegate? _setOverlayIcon;
    private SubclassProcDelegate? _subclassProc;
    private uint _taskbarButtonCreated;
    private int _count;

    public void Attach(IntPtr windowHandle)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == IntPtr.Zero) return;
        _windowHandle = windowHandle;
        _taskbarButtonCreated = RegisterWindowMessage("TaskbarButtonCreated");
        _subclassProc = WindowProc;
        if (!SetWindowSubclass(windowHandle, _subclassProc, UIntPtr.Zero, UIntPtr.Zero))
            Trace.TraceWarning("Could not watch taskbar recreation; the unread badge may need a count change to reappear.");
        SetCount(_count);
    }

    public void SetCount(int count)
    {
        _count = Math.Max(0, count);
        if (!OperatingSystem.IsWindows() || _windowHandle == IntPtr.Zero || !EnsureTaskbar()) return;

        var icon = IntPtr.Zero;
        if (_count > 0)
        {
            var label = _count > 9 ? "9plus" : _count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Badges", $"{label}.ico");
            icon = LoadImage(IntPtr.Zero, path, ImageIcon, 16, 16, LoadFromFile);
            if (icon == IntPtr.Zero)
            {
                Trace.TraceWarning($"Could not load the taskbar badge icon: {path}");
                return;
            }
        }

        try
        {
            var description = _count == 0 ? null : $"{_count} unread chat notifications";
            var result = _setOverlayIcon!(_taskbar, _windowHandle, icon, description);
            if (result < 0) Trace.TraceWarning($"Could not update taskbar badge: 0x{result:X8}");
        }
        finally
        {
            if (icon != IntPtr.Zero) DestroyIcon(icon);
        }
    }

    public void Detach()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (_windowHandle != IntPtr.Zero)
        {
            SetCount(0);
            if (_subclassProc is not null) RemoveWindowSubclass(_windowHandle, _subclassProc, UIntPtr.Zero);
            _windowHandle = IntPtr.Zero;
        }
        _subclassProc = null;
        _setOverlayIcon = null;
        if (_taskbar != IntPtr.Zero)
        {
            Marshal.Release(_taskbar);
            _taskbar = IntPtr.Zero;
        }
    }

    private bool EnsureTaskbar()
    {
        if (_taskbar != IntPtr.Zero) return true;
        var classId = TaskbarClass;
        var interfaceId = TaskbarInterface;
        var result = CoCreateInstance(in classId, IntPtr.Zero, InProcessServer, in interfaceId, out _taskbar);
        if (result < 0 || _taskbar == IntPtr.Zero)
        {
            Trace.TraceWarning($"Could not create the Windows taskbar interface: 0x{result:X8}");
            return false;
        }

        var vtable = Marshal.ReadIntPtr(_taskbar);
        var initialize = Marshal.GetDelegateForFunctionPointer<HrInitDelegate>(Marshal.ReadIntPtr(vtable, HrInitSlot * IntPtr.Size));
        result = initialize(_taskbar);
        if (result < 0)
        {
            Trace.TraceWarning($"Could not initialize the Windows taskbar interface: 0x{result:X8}");
            Marshal.Release(_taskbar);
            _taskbar = IntPtr.Zero;
            return false;
        }
        _setOverlayIcon = Marshal.GetDelegateForFunctionPointer<SetOverlayIconDelegate>(
            Marshal.ReadIntPtr(vtable, SetOverlayIconSlot * IntPtr.Size));
        return true;
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (message == _taskbarButtonCreated && _taskbarButtonCreated != 0) SetCount(_count);
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int HrInitDelegate(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int SetOverlayIconDelegate(IntPtr self, IntPtr window, IntPtr icon, string? description);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr SubclassProcDelegate(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid classId, IntPtr outer, uint context, in Guid interfaceId, out IntPtr instance);

    [LibraryImport("user32.dll", EntryPoint = "LoadImageW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr icon);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessage(string message);

    [LibraryImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(IntPtr window, SubclassProcDelegate callback, UIntPtr id, UIntPtr data);

    [LibraryImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveWindowSubclass(IntPtr window, SubclassProcDelegate callback, UIntPtr id);

    [LibraryImport("comctl32.dll")]
    private static partial IntPtr DefSubclassProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
}
