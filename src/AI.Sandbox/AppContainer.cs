namespace AI.Sandbox;

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

/// <summary>
/// Windows AppContainer: the launcher starts the program in a container, which sees only what an
/// ACL grants it, and waits for it. Each path of a policy is granted to a capability derived from
/// the path and its access rather than to the container itself, and a launch gets only the
/// capabilities of its own policy. So one container serves every policy, a path is granted once,
/// and a program never reaches a directory another policy opened.
/// </summary>
/// <remarks>
/// The grants are inheritable access entries added to the paths' ACLs, which Windows copies to
/// everything below them the first time; they stay, so later launches with the same path start at
/// once. The container itself reads the <c>Windows</c> and <c>Program Files</c> directories through
/// <c>ALL APPLICATION PACKAGES</c>, which is what lets an ordinary program load at all.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static partial class AppContainer
{
    private const string ContainerName = "AI.Client.Sandbox";
    private const int AlreadyExists = unchecked((int)0x800700B7);

    private const uint ExtendedStartupInfoPresent = 0x80000;
    private const uint CreateSuspended = 0x4;
    private const uint CreateUnicodeEnvironment = 0x400;
    private const int UseStandardHandles = 0x100;
    private const uint HandleFlagInherit = 0x1;
    private const uint Infinite = 0xFFFFFFFF;
    private const uint GroupEnabled = 0x4;

    private static readonly nint SecurityCapabilitiesAttribute = 0x20009;
    private static readonly nint HandleListAttribute = 0x20002;

    private const int ExtendedLimitInformation = 9;
    private const uint LimitKillOnJobClose = 0x2000;

    /// <summary>The well-known network capabilities: internet client, internet server, home and work networks.</summary>
    private static readonly SecurityIdentifier[] Network =
    [
        new("S-1-15-3-1"),
        new("S-1-15-3-2"),
        new("S-1-15-3-3")
    ];

    public static int Run(SandboxPolicy policy, string executable, IReadOnlyList<string> arguments)
    {
        var container = ContainerSid();
        var capabilities = new List<SecurityIdentifier>();
        foreach (var entry in policy.Paths)
        {
            if (Grant(entry) is { } capability)
            {
                capabilities.Add(capability);
            }
        }

        if (policy.AllowNetwork)
        {
            capabilities.AddRange(Network);
        }

        return Launch(container, capabilities, executable, arguments);
    }

    /// <summary>
    /// The capability that opens <paramref name="entry"/>, granted on the path if it is not yet; or
    /// <c>null</c> when the path does not exist or is readable by every container already.
    /// </summary>
    private static SecurityIdentifier? Grant(SandboxPath entry)
    {
        var directory = Directory.Exists(entry.Path);
        if (!directory && !File.Exists(entry.Path))
        {
            return null;
        }

        if (entry.Access == SandboxAccess.Read && IsSystemLocation(entry.Path))
        {
            return null;
        }

        var capability = CapabilitySid(entry);
        var rights = entry.Access == SandboxAccess.ReadWrite
            ? FileSystemRights.Modify | FileSystemRights.Synchronize
            : FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize;
        try
        {
            if (directory)
            {
                var info = new DirectoryInfo(entry.Path);
                var security = info.GetAccessControl(AccessControlSections.Access);
                if (!Granted(security, capability, rights))
                {
                    security.AddAccessRule(new FileSystemAccessRule(capability, rights,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    info.SetAccessControl(security);
                }
            }
            else
            {
                var info = new FileInfo(entry.Path);
                var security = info.GetAccessControl(AccessControlSections.Access);
                if (!Granted(security, capability, rights))
                {
                    security.AddAccessRule(new FileSystemAccessRule(capability, rights, AccessControlType.Allow));
                    info.SetAccessControl(security);
                }
            }
        }
        catch (Exception error) when (error is UnauthorizedAccessException or InvalidOperationException or IOException && entry.Access == SandboxAccess.Read)
        {
            // A directory the user may not change the ACL of, such as one owned by the system, is
            // left closed rather than failing the whole launch over something the program may not need.
            Console.Error.WriteLine($"ai-sandbox: cannot grant read access to {entry.Path}: {error.Message}");
            return null;
        }

        return capability;
    }

    private static bool Granted(FileSystemSecurity security, SecurityIdentifier capability, FileSystemRights rights) =>
        security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .OfType<FileSystemAccessRule>()
            .Any(rule => rule.AccessControlType == AccessControlType.Allow
                         && rule.IdentityReference.Equals(capability)
                         && (rule.FileSystemRights & rights) == rights);

    private static bool IsSystemLocation(string path)
    {
        foreach (var folder in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var root = Environment.GetFolderPath(folder);
            if (root.Length > 0 && (path.Equals(root, StringComparison.OrdinalIgnoreCase)
                                    || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A capability for one path and access, stable across launches.</summary>
    private static SecurityIdentifier CapabilitySid(SandboxPath entry)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Path.ToUpperInvariant())))[..32];
        var name = $"{ContainerName}.{entry.Access}.{digest}";
        if (!DeriveCapabilitySidsFromName(name, out var groups, out var groupCount, out var sids, out var count))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "DeriveCapabilitySidsFromName failed");
        }

        try
        {
            return new SecurityIdentifier(Marshal.ReadIntPtr(sids));
        }
        finally
        {
            Free(groups, groupCount);
            Free(sids, count);
        }
    }

    private static void Free(nint array, uint count)
    {
        for (var index = 0; index < count; index++)
        {
            _ = LocalFree(Marshal.ReadIntPtr(array, index * IntPtr.Size));
        }

        _ = LocalFree(array);
    }

    private static SecurityIdentifier ContainerSid()
    {
        var result = CreateAppContainerProfile(ContainerName, "AI Client sandbox",
            "Runs commands and scripts of AI Client confined to the directories a chat may use.", 0, 0, out var sid);
        if (result == AlreadyExists)
        {
            result = DeriveAppContainerSidFromAppContainerName(ContainerName, out sid);
        }

        if (result != 0)
        {
            throw new Win32Exception(result, "Cannot create the AppContainer profile");
        }

        try
        {
            return new SecurityIdentifier(sid);
        }
        finally
        {
            _ = FreeSid(sid);
        }
    }

    /// <summary>
    /// Starts the program suspended in the container with this process's standard handles, puts it
    /// in a job that dies with this process, lets it run and waits for it.
    /// </summary>
    private static unsafe int Launch(SecurityIdentifier container, List<SecurityIdentifier> capabilities, string executable, IReadOnlyList<string> arguments)
    {
        var allocations = new List<nint>();
        nint attributes = 0;
        try
        {
            var containerSid = Allocate(container, allocations);
            var capabilityArray = Marshal.AllocHGlobal(Math.Max(1, capabilities.Count) * sizeof(SidAndAttributes));
            allocations.Add(capabilityArray);
            for (var index = 0; index < capabilities.Count; index++)
            {
                ((SidAndAttributes*)capabilityArray)[index] = new SidAndAttributes { Sid = Allocate(capabilities[index], allocations), Attributes = GroupEnabled };
            }

            var security = (SecurityCapabilities*)Marshal.AllocHGlobal(sizeof(SecurityCapabilities));
            allocations.Add((nint)security);
            *security = new SecurityCapabilities
            {
                AppContainerSid = containerSid,
                Capabilities = capabilityArray,
                CapabilityCount = (uint)capabilities.Count
            };

            var handles = StandardHandles();
            var handleArray = Marshal.AllocHGlobal(Math.Max(1, handles.Length) * IntPtr.Size);
            allocations.Add(handleArray);
            for (var index = 0; index < handles.Length; index++)
            {
                ((nint*)handleArray)[index] = handles[index];
            }

            nint size = 0;
            _ = InitializeProcThreadAttributeList(0, 2, 0, ref size);
            attributes = Marshal.AllocHGlobal(size);
            Check(InitializeProcThreadAttributeList(attributes, 2, 0, ref size));
            Check(UpdateProcThreadAttribute(attributes, 0, SecurityCapabilitiesAttribute, (nint)security, sizeof(SecurityCapabilities), 0, 0));
            if (handles.Length > 0)
            {
                Check(UpdateProcThreadAttribute(attributes, 0, HandleListAttribute, handleArray, handles.Length * IntPtr.Size, 0, 0));
            }

            var startup = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    Size = sizeof(StartupInfoEx),
                    Flags = UseStandardHandles,
                    StandardInput = GetStdHandle(-10),
                    StandardOutput = GetStdHandle(-11),
                    StandardError = GetStdHandle(-12)
                },
                AttributeList = attributes
            };

            var commandLine = CommandLine.Build(executable, arguments).ToCharArray();
            ProcessInformation information;
            fixed (char* line = commandLine)
            {
                Check(CreateProcessW(null, line, 0, 0, handles.Length > 0, ExtendedStartupInfoPresent | CreateSuspended | CreateUnicodeEnvironment,
                    0, null, ref startup, out information));
            }

            using var job = CreateJobObjectW(0, null);
            try
            {
                var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = LimitKillOnJobClose } };
                // Without the job a launcher killed on its own would leave the program running.
                if (job.IsInvalid
                    || !SetInformationJobObject(job, ExtendedLimitInformation, in limits, (uint)sizeof(ExtendedLimits))
                    || !AssignProcessToJobObject(job, information.Process))
                {
                    var error = Marshal.GetLastPInvokeError();
                    _ = TerminateProcess(information.Process, 1);
                    throw new Win32Exception(error, "Cannot place the confined program in a job");
                }

                _ = ResumeThread(information.Thread);
                _ = WaitForSingleObject(information.Process, Infinite);
                return GetExitCodeProcess(information.Process, out var exitCode) ? (int)exitCode : 1;
            }
            finally
            {
                _ = CloseHandle(information.Thread);
                _ = CloseHandle(information.Process);
            }
        }
        finally
        {
            if (attributes != 0)
            {
                DeleteProcThreadAttributeList(attributes);
                Marshal.FreeHGlobal(attributes);
            }

            foreach (var allocation in allocations)
            {
                Marshal.FreeHGlobal(allocation);
            }
        }
    }

    /// <summary>
    /// The standard handles worth handing on, made inheritable and without duplicates: output and
    /// error are often one pipe, and an attribute list naming a handle twice is refused.
    /// </summary>
    private static nint[] StandardHandles()
    {
        var handles = new List<nint>();
        foreach (var which in new[] { -10, -11, -12 })
        {
            var handle = GetStdHandle(which);
            if (handle is 0 or -1 || handles.Contains(handle))
            {
                continue;
            }

            if (SetHandleInformation(handle, HandleFlagInherit, HandleFlagInherit))
            {
                handles.Add(handle);
            }
        }

        return handles.ToArray();
    }

    private static nint Allocate(SecurityIdentifier sid, List<nint> allocations)
    {
        var bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
        var memory = Marshal.AllocHGlobal(bytes.Length);
        allocations.Add(memory);
        Marshal.Copy(bytes, 0, memory, bytes.Length);
        return memory;
    }

    private static void Check(bool succeeded)
    {
        if (!succeeded)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public nint Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityCapabilities
    {
        public nint AppContainerSid;
        public nint Capabilities;
        public uint CapabilityCount;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
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
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public nint AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
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

    [LibraryImport("userenv.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CreateAppContainerProfile(string name, string displayName, string description, nint capabilities, uint count, out nint sid);

    [LibraryImport("userenv.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int DeriveAppContainerSidFromAppContainerName(string name, out nint sid);

    [LibraryImport("advapi32.dll")]
    private static partial nint FreeSid(nint sid);

    [LibraryImport("kernelbase.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeriveCapabilitySidsFromName(string name, out nint groupSids, out uint groupCount, out nint sids, out uint count);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);

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
    private static unsafe partial bool CreateProcessW(string? application, char* commandLine, nint processAttributes, nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, nint environment, string? currentDirectory,
        ref StartupInfoEx startupInfo, out ProcessInformation information);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GetStdHandle(int which);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetHandleInformation(nint handle, uint mask, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint ResumeThread(nint thread);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(nint process, out uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(nint process, uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial Microsoft.Win32.SafeHandles.SafeFileHandle CreateJobObjectW(nint attributes, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(Microsoft.Win32.SafeHandles.SafeFileHandle job, int informationClass, in ExtendedLimits limits, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(Microsoft.Win32.SafeHandles.SafeFileHandle job, nint process);
}
