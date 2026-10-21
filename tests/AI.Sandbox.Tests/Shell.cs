namespace AI.Sandbox.Tests;

using System.Diagnostics;
using Xunit;

/// <summary>
/// Commands the tests run through the platform shell, which every machine has and every sandbox
/// lets start: <c>cmd.exe</c> on Windows, <c>/bin/sh</c> elsewhere.
/// </summary>
internal static class Shell
{
    /// <summary>
    /// Set on CI so a machine that cannot enforce a policy fails the run instead of skipping the
    /// tests that prove the sandbox works.
    /// </summary>
    public const string RequiredVariable = "AI_SANDBOX_REQUIRED";

    public static ProcessStartInfo Command(string command, string? workingDirectory = null)
    {
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { ArgumentList = { "/d", "/c", command } }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", command } };
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.RedirectStandardInput = true;
        start.WorkingDirectory = workingDirectory ?? "";
        return start;
    }

    // cmd does not split its command line the way the C runtime quotes it, so Windows paths go unquoted.
    public static string Read(string file) => OperatingSystem.IsWindows() ? $"type {file}" : $"cat '{file}'";

    public static string Write(string file, string text) => OperatingSystem.IsWindows() ? $"echo {text}> {file}" : $"echo {text} > '{file}'";

    public static string Exit(int code) => $"exit {code}";

    public static string PrintVariable(string name) => OperatingSystem.IsWindows() ? $"echo %{name}%" : $"echo ${name}";

    public static string PrintDirectory() => OperatingSystem.IsWindows() ? "cd" : "pwd";

    /// <summary>
    /// A command that runs far longer than any test waits. On Windows a loop rather than <c>ping</c>
    /// or <c>timeout</c>: an AppContainer may not send ICMP, and <c>timeout</c> refuses redirected input.
    /// </summary>
    public static string Sleep() => OperatingSystem.IsWindows() ? "for /L %i in (1,1,2000000000) do @rem" : "sleep 60";

    /// <summary>A command that starts one more process of the shell before it finishes.</summary>
    public static string Nested(string command) => OperatingSystem.IsWindows() ? $"cmd /d /c {command}" : $"/bin/sh -c \"{command}\"";

    /// <summary>Skips a test the machine cannot run, unless CI requires it to run.</summary>
    public static void RequireSandbox(IProcessLauncher launcher)
    {
        if (launcher.Support.Available)
        {
            return;
        }

        var reason = $"{launcher.Support.Mechanism} is not available: {launcher.Support.Reason}";
        Assert.False(Environment.GetEnvironmentVariable(RequiredVariable) == "1", reason);
        Assert.Skip(reason);
    }
}
