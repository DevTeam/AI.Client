namespace AI.Sandbox;

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

/// <summary>
/// Holds the process tree a tool call starts within <see cref="ChildProcessLimits"/>. On Windows
/// this is a Job Object: closing it kills every process still in it, even descendants whose parent
/// already exited, and it caps the tree's committed memory, its live processes and its CPU share.
/// Elsewhere only the priority is lowered; there is no equivalent the server can apply to a tree
/// it does not start through a wrapper.
/// </summary>
/// <remarks>
/// This is resource and lifetime management, not a security boundary: the tree still runs with
/// everything the account can do. The OS closes the job handle if the server crashes or loses its
/// Host, which takes the tree down with it.
/// </remarks>
public sealed partial class ChildProcessJob : IDisposable
{
    /// <summary><see cref="LimitExceeded"/> when an allocation was refused by the memory limit.</summary>
    public const string Memory = "memory";

    /// <summary><see cref="LimitExceeded"/> when a process start was refused by the process limit.</summary>
    public const string Processes = "processes";

    private readonly WindowsJob? _windows;

    public ChildProcessJob(ChildProcessLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (OperatingSystem.IsWindows())
        {
            _windows = new WindowsJob(limits);
        }
    }

    /// <summary>
    /// The limit the tree ran into, or <c>null</c>. Read it after the tree has exited: the job
    /// reports a limit as soon as it refuses something, and the program decides on its own whether
    /// that ends it.
    /// </summary>
    public string? LimitExceeded => OperatingSystem.IsWindows() ? _windows?.LimitExceeded : null;

    /// <summary>The job itself, for a program created inside it rather than attached after it started.</summary>
    [SupportedOSPlatform("windows")]
    internal SafeFileHandle Handle => _windows!.Handle;

    /// <summary>
    /// Puts a started process into the job. Its descendants join it as they start. A process that
    /// cannot be placed is the caller's to kill: an unlimited tree is not what was asked for.
    /// </summary>
    public void Attach(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (OperatingSystem.IsWindows())
        {
            _windows!.Attach(process);
            return;
        }

        try
        {
            // Inherited by the descendants this process starts from now on.
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // The process exited already, or the OS refused; neither is worth failing the call for.
        }
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            _windows?.Dispose();
        }
    }

    [SupportedOSPlatform("windows")]
    private sealed partial class WindowsJob : IDisposable
    {
        private const int ExtendedLimitInformation = 9;
        private const int AssociateCompletionPortInformation = 7;
        private const int CpuRateControlInformation = 15;

        private const uint LimitActiveProcess = 0x8;
        private const uint LimitPriorityClass = 0x20;
        private const uint LimitJobMemory = 0x200;
        private const uint LimitKillOnJobClose = 0x2000;
        private const uint BelowNormalPriorityClass = 0x4000;

        private const uint CpuRateControlEnable = 0x1;
        private const uint CpuRateControlHardCap = 0x4;

        private const uint MessageActiveProcessLimit = 3;
        private const uint MessageJobMemoryLimit = 10;

        private readonly SafeFileHandle _handle;
        private readonly SafeFileHandle _port;
        private string? _exceeded;

        public WindowsJob(ChildProcessLimits limits)
        {
            _handle = CreateJobObjectW(0, null);
            if (_handle.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            try
            {
                _port = CreateIoCompletionPort(new nint(-1), 0, 0, 1);
                if (_port.IsInvalid)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                // The port is where the job says which limit it enforced; without it a refused
                // allocation is indistinguishable from any other crash of the program.
                var port = new CompletionPort { Key = 0, Port = _port.DangerousGetHandle() };
                Check(SetInformationJobObject(_handle, AssociateCompletionPortInformation, in port, (uint)Marshal.SizeOf<CompletionPort>()));

                var extended = new ExtendedLimits
                {
                    Basic = new BasicLimits
                    {
                        Flags = LimitKillOnJobClose | LimitJobMemory | LimitActiveProcess | LimitPriorityClass,
                        ActiveProcessLimit = (uint)limits.Processes,
                        PriorityClass = BelowNormalPriorityClass
                    },
                    JobMemory = (nuint)limits.MemoryBytes
                };
                Check(SetInformationJobObject(_handle, ExtendedLimitInformation, in extended, (uint)Marshal.SizeOf<ExtendedLimits>()));

                var cpu = new CpuRate
                {
                    Flags = CpuRateControlEnable | CpuRateControlHardCap,
                    // Hundredths of a percent of all processors.
                    Rate = ChildProcessLimits.CpuPercent * 100
                };
                Check(SetInformationJobObject(_handle, CpuRateControlInformation, in cpu, (uint)Marshal.SizeOf<CpuRate>()));
            }
            catch
            {
                _port?.Dispose();
                _handle.Dispose();
                throw;
            }
        }

        public string? LimitExceeded
        {
            get
            {
                while (GetQueuedCompletionStatus(_port, out var message, out _, out _, 0))
                {
                    _exceeded ??= message switch
                    {
                        MessageJobMemoryLimit => Memory,
                        MessageActiveProcessLimit => Processes,
                        _ => null
                    };
                }

                return _exceeded;
            }
        }

        public SafeFileHandle Handle => _handle;

        public void Attach(Process process) =>
            Check(AssignProcessToJobObject(_handle, process.SafeHandle));

        public void Dispose()
        {
            _handle.Dispose();
            _port.Dispose();
        }

        private static void Check(bool succeeded)
        {
            if (!succeeded)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }

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

        [StructLayout(LayoutKind.Sequential)]
        private struct CompletionPort
        {
            public nint Key;
            public nint Port;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CpuRate
        {
            public uint Flags;
            public uint Rate;
        }

        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        private static partial SafeFileHandle CreateJobObjectW(nint attributes, string? name);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial SafeFileHandle CreateIoCompletionPort(nint file, nint existingPort, nuint key, uint threads);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetQueuedCompletionStatus(SafeFileHandle port, out uint bytes, out nuint key, out nint overlapped, uint milliseconds);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetInformationJobObject(SafeFileHandle job, int informationClass, in ExtendedLimits limits, uint length);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetInformationJobObject(SafeFileHandle job, int informationClass, in CompletionPort port, uint length);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetInformationJobObject(SafeFileHandle job, int informationClass, in CpuRate rate, uint length);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
    }
}
