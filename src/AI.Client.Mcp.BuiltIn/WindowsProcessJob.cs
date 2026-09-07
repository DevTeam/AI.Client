using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace AI.Client.Mcp.BuiltIn;

// Closing the job also kills descendants whose original parent already exited.
// The OS closes this handle if the MCP server crashes or loses its Host.
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsProcessJob : IDisposable
{
    private readonly SafeFileHandle _handle;

    public WindowsProcessJob()
    {
        _handle = CreateJobObjectW(0, null);
        if (_handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE
        if (SetInformationJobObject(_handle, 9, in limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        {
            return;
        }

        var error = Marshal.GetLastPInvokeError();
        _handle.Dispose();
        throw new Win32Exception(error);
    }

    public void Attach(Process process)
    {
        if (!AssignProcessToJobObject(_handle, process.SafeHandle)) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    public void Dispose() => _handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime;
        public long JobTime;
        public uint Flags;
        public nuint MinimumWorkingSet;
        public nuint MaximumWorkingSet;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadOperations;
        public ulong WriteOperations;
        public ulong OtherOperations;
        public ulong ReadBytes;
        public ulong WriteBytes;
        public ulong OtherBytes;
        public nuint ProcessMemory;
        public nuint JobMemory;
        public nuint PeakProcessMemory;
        public nuint PeakJobMemory;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateJobObjectW(nint attributes, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(SafeFileHandle job, int informationClass, in ExtendedLimits limits, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
}
