namespace AI.Sandbox;

using System.Collections;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

/// <summary>
/// Linux Landlock: the launcher restricts its own thread to the policy's paths and then replaces
/// itself with the program, which keeps the restriction together with everything it starts. The
/// restriction cannot be lifted from inside. Only the file system is handled unless the policy
/// closes the network, which needs Landlock ABI 4.
/// </summary>
/// <remarks>
/// <c>landlock_restrict_self</c> restricts the calling thread only, so the thread that restricts
/// itself is the one that calls <c>execve</c>; nothing between the two may hop threads.
/// </remarks>
internal static partial class Landlock
{
    // System call numbers shared by x86-64 and the generic table arm64 uses.
    private const long CreateRuleset = 444;
    private const long AddRule = 445;
    private const long RestrictSelf = 446;

    private const uint CreateRulesetVersion = 1;
    private const int RulePathBeneath = 1;
    private const int SetNoNewPrivileges = 38;

    private const int OpenPath = 0x200000;
    private const int OpenCloseOnExec = 0x80000;

    private const ulong Execute = 1UL << 0;
    private const ulong WriteFile = 1UL << 1;
    private const ulong ReadFile = 1UL << 2;
    private const ulong ReadDirectory = 1UL << 3;
    private const ulong Refer = 1UL << 13;
    private const ulong Truncate = 1UL << 14;
    private const ulong IoctlDevice = 1UL << 15;

    private const ulong ReadRights = Execute | ReadFile | ReadDirectory;

    /// <summary>The rights a rule for a file, rather than a directory, may carry.</summary>
    private const ulong FileRights = Execute | WriteFile | ReadFile | Truncate | IoctlDevice;

    private const ulong NetworkBindTcp = 1UL << 0;
    private const ulong NetworkConnectTcp = 1UL << 1;

    /// <summary>The Landlock ABI version of the running kernel, or 0 when Landlock is unavailable.</summary>
    public static int Abi()
    {
        if (!OperatingSystem.IsLinux())
        {
            return 0;
        }

        try
        {
            var version = syscall(CreateRuleset, 0, 0, (nint)CreateRulesetVersion, 0);
            return version > 0 ? (int)version : 0;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return 0;
        }
    }

    /// <summary>Restricts this thread to <paramref name="policy"/> and executes the program; returns only on failure.</summary>
    [SupportedOSPlatform("linux")]
    public static unsafe int Exec(SandboxPolicy policy, string executable, string[] arguments)
    {
        var abi = Abi();
        if (abi < 1)
        {
            throw new SandboxUnavailableException("The kernel does not support Landlock.");
        }

        var handled = Handled(abi);
        var attribute = new RulesetAttribute { HandledAccessFs = handled };
        var size = (nuint)sizeof(ulong);
        if (!policy.AllowNetwork)
        {
            if (abi < 4)
            {
                throw new SandboxUnavailableException("Closing the network needs Landlock ABI 4 (Linux 6.7) or later.");
            }

            attribute.HandledAccessNet = NetworkBindTcp | NetworkConnectTcp;
            size = (nuint)(2 * sizeof(ulong));
        }

        var ruleset = (int)Check(syscall(CreateRuleset, (nint)(&attribute), (nint)size, 0, 0), "landlock_create_ruleset");
        foreach (var entry in policy.Paths)
        {
            var directory = Directory.Exists(entry.Path);
            if (!directory && !File.Exists(entry.Path))
            {
                continue;
            }

            var descriptor = open(entry.Path, OpenPath | OpenCloseOnExec);
            if (descriptor < 0)
            {
                continue;
            }

            try
            {
                var allowed = (entry.Access == SandboxAccess.ReadWrite ? handled : ReadRights) & handled;
                var rule = new PathBeneathAttribute
                {
                    AllowedAccess = directory ? allowed : allowed & FileRights,
                    ParentFd = descriptor
                };
                Check(syscall(AddRule, ruleset, RulePathBeneath, (nint)(&rule), 0), $"landlock_add_rule {entry.Path}");
            }
            finally
            {
                _ = close(descriptor);
            }
        }

        Check(prctl(SetNoNewPrivileges, 1, 0, 0, 0), "prctl(PR_SET_NO_NEW_PRIVS)");
        Check(syscall(RestrictSelf, ruleset, 0, 0, 0), "landlock_restrict_self");
        _ = close(ruleset);

        var argv = Strings([executable, .. arguments]);
        var environment = Strings(Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .Where(entry => entry.Value is not null)
            .Select(entry => $"{entry.Key}={entry.Value}")
            .ToArray());
        _ = execve(executable, argv, environment);
        throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Cannot execute {executable}");
    }

    /// <summary>Every file-system right the kernel's ABI knows, so none of them is left unrestricted.</summary>
    private static ulong Handled(int abi)
    {
        var rights = (1UL << 13) - 1;
        if (abi >= 2) rights |= Refer;
        if (abi >= 3) rights |= Truncate;
        if (abi >= 5) rights |= IoctlDevice;
        return rights;
    }

    private static long Check(long result, string call) =>
        result >= 0 ? result : throw new Win32Exception(Marshal.GetLastPInvokeError(), $"{call} failed");

    /// <summary>A null-terminated array of UTF-8 strings; it lives until the process image is replaced.</summary>
    private static nint Strings(string[] values)
    {
        var array = Marshal.AllocHGlobal((values.Length + 1) * IntPtr.Size);
        for (var index = 0; index < values.Length; index++)
        {
            Marshal.WriteIntPtr(array, index * IntPtr.Size, Marshal.StringToCoTaskMemUTF8(values[index]));
        }

        Marshal.WriteIntPtr(array, values.Length * IntPtr.Size, 0);
        return array;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RulesetAttribute
    {
        public ulong HandledAccessFs;
        public ulong HandledAccessNet;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct PathBeneathAttribute
    {
        public ulong AllowedAccess;
        public int ParentFd;
    }

    [LibraryImport("libc", SetLastError = true)]
    private static partial long syscall(long number, nint first, nint second, nint third, nint fourth);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int prctl(int option, nuint second, nuint third, nuint fourth, nuint fifth);

    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int open(string path, int flags);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int close(int descriptor);

    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int execve(string path, nint arguments, nint environment);
}
