namespace AI.Sandbox;

using System.Runtime.InteropServices;

/// <summary>
/// The read-only locations every confined program needs to start, added to the caller's policy:
/// the program's own directory, the .NET installation the launcher and .NET programs load from, and
/// the OS directories with libraries, configuration and devices. On Windows the OS directories need
/// nothing: an AppContainer reads <c>Windows</c> and <c>Program Files</c> already.
/// </summary>
internal static class SystemLocations
{
    private static readonly string[] LinuxRead = ["/usr", "/lib", "/lib32", "/lib64", "/libx32", "/bin", "/sbin", "/etc", "/opt", "/proc", "/sys", "/run"];

    private static readonly string[] MacRead = ["/usr", "/System", "/Library", "/bin", "/sbin", "/private/etc", "/private/var/db", "/opt"];

    /// <summary>Devices programs write to as a matter of course, such as <c>/dev/null</c>.</summary>
    private const string Devices = "/dev";

    public static SandboxPolicy Complete(SandboxPolicy policy, string executable)
    {
        var paths = new List<SandboxPath>(policy.Paths);
        if (Path.GetDirectoryName(executable) is { Length: > 0 } programDirectory)
        {
            paths.Add(new SandboxPath(programDirectory, SandboxAccess.Read));
        }

        foreach (var runtime in DotnetRoots())
        {
            paths.Add(new SandboxPath(runtime, SandboxAccess.Read));
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            paths.AddRange((OperatingSystem.IsLinux() ? LinuxRead : MacRead)
                .Where(Directory.Exists)
                .Select(path => new SandboxPath(path, SandboxAccess.Read)));
            paths.Add(new SandboxPath(Devices, SandboxAccess.ReadWrite));
        }

        return (policy with { Paths = paths }).Normalize();
    }

    /// <summary>
    /// The installation the running runtime came from, three levels above its
    /// <c>shared/Microsoft.NETCore.App/&lt;version&gt;</c> directory, and <c>DOTNET_ROOT</c> when it
    /// names another one.
    /// </summary>
    private static IEnumerable<string> DotnetRoots()
    {
        var runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
        if (runtime.Parent?.Parent?.Parent is { } root && File.Exists(Path.Combine(root.FullName, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet")))
        {
            yield return root.FullName;
        }
        else
        {
            yield return runtime.FullName;
        }

        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } configured && Path.IsPathFullyQualified(configured) && Directory.Exists(configured))
        {
            yield return configured;
        }
    }
}
