namespace AI.Sandbox;

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

/// <summary>
/// The Win32 process creation both Windows starts share: the job-bounded start of every program
/// (<see cref="WindowsProcess"/>) and the AppContainer launcher (<see cref="AppContainer"/>).
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsNative
{
    public const uint ExtendedStartupInfoPresent = 0x80000;
    public const uint CreateSuspended = 0x4;
    public const uint CreateUnicodeEnvironment = 0x400;
    public const uint CreateNoWindow = 0x08000000;
    public const int UseStandardHandles = 0x100;
    public const uint HandleFlagInherit = 0x1;

    public static readonly nint HandleListAttribute = 0x20002;
    public static readonly nint JobListAttribute = 0x2000D;
    public static readonly nint SecurityCapabilitiesAttribute = 0x20009;

    /// <summary>
    /// An attribute list for <c>CreateProcessW</c>, freed with everything it points at. Each value
    /// is copied into memory the list owns, because the list keeps only pointers and has to stay
    /// valid until the process is created.
    /// </summary>
    public sealed class AttributeList : IDisposable
    {
        private readonly List<nint> _allocations = [];

        public AttributeList(int count)
        {
            nint size = 0;
            _ = InitializeProcThreadAttributeList(0, count, 0, ref size);
            Handle = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(Handle, count, 0, ref size))
            {
                var error = Marshal.GetLastPInvokeError();
                Marshal.FreeHGlobal(Handle);
                throw new Win32Exception(error);
            }
        }

        public nint Handle { get; }

        /// <summary>Sets an attribute whose value is a block of handles or pointers.</summary>
        public void Add(nint attribute, ReadOnlySpan<nint> values)
        {
            var memory = Marshal.AllocHGlobal(Math.Max(1, values.Length) * IntPtr.Size);
            _allocations.Add(memory);
            for (var index = 0; index < values.Length; index++)
            {
                Marshal.WriteIntPtr(memory, index * IntPtr.Size, values[index]);
            }

            Add(attribute, memory, values.Length * IntPtr.Size);
        }

        /// <summary>Sets an attribute whose value already lives in memory that outlives the list.</summary>
        public void Add(nint attribute, nint value, int size) =>
            Check(UpdateProcThreadAttribute(Handle, 0, attribute, value, size, 0, 0));

        public void Dispose()
        {
            DeleteProcThreadAttributeList(Handle);
            Marshal.FreeHGlobal(Handle);
            foreach (var allocation in _allocations)
            {
                Marshal.FreeHGlobal(allocation);
            }
        }
    }

    public static void Check(bool succeeded)
    {
        if (!succeeded)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct StartupInfo
    {
        public int Size;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public nint Reserved2;
        public nint StandardInput;
        public nint StandardOutput;
        public nint StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public nint AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(nint list, int count, int flags, ref nint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(nint list, uint flags, nint attribute, nint value, nint size, nint previous, nint returnSize);

    [LibraryImport("kernel32.dll")]
    private static partial void DeleteProcThreadAttributeList(nint list);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool CreateProcessW(string? application, char* commandLine, nint processAttributes, nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, char* environment, string? currentDirectory,
        ref StartupInfoEx startupInfo, out ProcessInformation information);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, nint attributes, int size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint GetStdHandle(int which);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetHandleInformation(nint handle, uint mask, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint ResumeThread(nint thread);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateProcess(nint process, uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);
}
