namespace AI.Sandbox;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

/// <summary>
/// Starts a confined process by starting something else in its place: on Windows and Linux this
/// assembly's own launcher, which confines itself or its child before the program runs, and on
/// macOS <c>sandbox-exec</c> with a generated profile. The caller still gets the
/// <see cref="Process"/> it would have got otherwise, with the redirected streams it asked for.
/// </summary>
/// <remarks>
/// The program is resolved to a full path here, before anything is confined, so the policy can let
/// the process read the program's own directory and so a missing program fails the start the same
/// way it does without a sandbox.
/// </remarks>
public sealed class ProcessLauncher : IProcessLauncher
{
    /// <summary>The argument that tells this assembly's entry point it is a launcher.</summary>
    internal const string PolicyArgument = "--policy";

    internal const string ProgramSeparator = "--";

    private const string SandboxExec = "/usr/bin/sandbox-exec";

    private static readonly Lazy<SandboxSupport> Detected = new(Detect);

    public SandboxSupport Support => Detected.Value;

    public ILaunchedProcess Start(ProcessLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        var start = launch.Start;
        if (start.UseShellExecute)
        {
            throw new ArgumentException("A bounded process cannot be started through the shell.", nameof(launch));
        }

        if (launch.TimeoutMs is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(launch), launch.TimeoutMs, "The timeout must be positive.");
        }

        var confined = launch.Policy is { } policy ? Confine(start, policy) : start;
        // Created before the process starts: a limit the OS cannot apply fails the call, not the machine.
        var job = new ChildProcessJob(launch.Limits ?? ChildProcessLimits.Default);
        try
        {
            return OperatingSystem.IsWindows() ? StartInJob(confined, job, launch.TimeoutMs) : StartAndAttach(confined, job, launch.TimeoutMs);
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    /// <summary>On Windows the program is created inside the job, before any of its code runs.</summary>
    [SupportedOSPlatform("windows")]
    private static LaunchedProcess StartInJob(ProcessStartInfo start, ChildProcessJob job, int? timeoutMs)
    {
        var started = WindowsProcess.Start(start, job.Handle);
        return new LaunchedProcess(started.Process, started.Input, started.Output, started.Error, job, started.Handle, timeoutMs);
    }

    /// <summary>
    /// Elsewhere the job only lowers the priority of what the program starts from now on, so the
    /// program is started as usual and given its job right after.
    /// </summary>
    private static LaunchedProcess StartAndAttach(ProcessStartInfo start, ChildProcessJob job, int? timeoutMs)
    {
        var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch
        {
            process.Dispose();
            throw;
        }

        var launched = new LaunchedProcess(process, process.StandardInput, process.StandardOutput, process.StandardError, job, null, timeoutMs);
        try
        {
            job.Attach(process);
        }
        catch
        {
            // An unbounded tree is not what was asked for.
            launched.Dispose();
            throw;
        }

        return launched;
    }

    private ProcessStartInfo Confine(ProcessStartInfo start, SandboxPolicy policy)
    {
        if (!Support.Available)
        {
            throw new SandboxUnavailableException($"The {Support.Mechanism} sandbox is not available on this machine: {Support.Reason}");
        }

        var executable = ExecutableResolver.Resolve(start.FileName, start.WorkingDirectory, start.Environment);
        var effective = SystemLocations.Complete(policy, executable);
        return OperatingSystem.IsMacOS()
            ? Wrap(start, SandboxExec, ["-p", SeatbeltProfile.Build(effective)], executable)
            : Wrap(start, LauncherPath(out var prefix), [.. prefix, PolicyArgument, effective.ToJson(), ProgramSeparator], executable);
    }

    /// <summary>
    /// A copy of <paramref name="start"/> that runs <paramref name="wrapper"/> with its own
    /// arguments, then the program and the program's arguments. Everything else about the start is
    /// the caller's: the wrapper hands the working directory, the environment and the standard
    /// streams on to the program unchanged.
    /// </summary>
    private static ProcessStartInfo Wrap(ProcessStartInfo start, string wrapper, IEnumerable<string> wrapperArguments, string executable)
    {
        var confined = new ProcessStartInfo(wrapper)
        {
            WorkingDirectory = start.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = start.CreateNoWindow,
            RedirectStandardInput = start.RedirectStandardInput,
            RedirectStandardOutput = start.RedirectStandardOutput,
            RedirectStandardError = start.RedirectStandardError,
            StandardInputEncoding = start.StandardInputEncoding,
            StandardOutputEncoding = start.StandardOutputEncoding,
            StandardErrorEncoding = start.StandardErrorEncoding
        };

        foreach (var argument in wrapperArguments)
        {
            confined.ArgumentList.Add(argument);
        }

        confined.ArgumentList.Add(executable);
        if (start.ArgumentList.Count > 0)
        {
            foreach (var argument in start.ArgumentList)
            {
                confined.ArgumentList.Add(argument);
            }
        }
        else if (!string.IsNullOrEmpty(start.Arguments))
        {
            throw new ArgumentException("A confined process takes its arguments through ArgumentList.", nameof(start));
        }

        confined.Environment.Clear();
        foreach (var (name, value) in start.Environment)
        {
            confined.Environment[name] = value;
        }

        return confined;
    }

    /// <summary>
    /// This assembly's apphost, which the build puts next to the assembly of every project that
    /// references it; without one, the same assembly through the <c>dotnet</c> muxer.
    /// </summary>
    private static string LauncherPath(out string[] prefix)
    {
        var assembly = typeof(ProcessLauncher).Assembly.Location;
        var directory = Path.GetDirectoryName(assembly) ?? AppContext.BaseDirectory;
        var apphost = Path.Combine(directory, "AI.Sandbox" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        if (File.Exists(apphost))
        {
            prefix = [];
            return apphost;
        }

        prefix = [assembly];
        return "dotnet";
    }

    private static SandboxSupport Detect()
    {
        if (OperatingSystem.IsWindows())
        {
            // AppContainer capabilities derived from names arrived with Windows 10.
            return OperatingSystem.IsWindowsVersionAtLeast(10)
                ? new SandboxSupport(true, "AppContainer", ClosesNetwork: true)
                : new SandboxSupport(false, "AppContainer", "Windows 10 or later is required.");
        }

        if (OperatingSystem.IsLinux())
        {
            var abi = Landlock.Abi();
            return abi > 0
                ? new SandboxSupport(true, $"Landlock ABI {abi}", ClosesNetwork: abi >= 4)
                : new SandboxSupport(false, "Landlock", "The kernel does not support Landlock or it is disabled (Linux 5.13 or later with Landlock enabled is required).");
        }

        if (OperatingSystem.IsMacOS())
        {
            return File.Exists(SandboxExec)
                ? new SandboxSupport(true, "Seatbelt", ClosesNetwork: true)
                : new SandboxSupport(false, "Seatbelt", $"{SandboxExec} is missing.");
        }

        return new SandboxSupport(false, RuntimeInformation.OSDescription, "This operating system has no supported sandbox.");
    }
}
