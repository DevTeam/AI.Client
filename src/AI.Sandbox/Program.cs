using AI.Sandbox;

return SandboxLauncher.Run(args);

namespace AI.Sandbox
{
    using System.ComponentModel;
    using System.Text.Json;

    /// <summary>
    /// The launcher <see cref="ProcessLauncher"/> starts in place of a confined program:
    /// <c>--policy &lt;json&gt; -- &lt;program&gt; [arguments]</c>. On Linux it confines itself and becomes the
    /// program; on Windows it starts the program in an AppContainer and exits with its exit code.
    /// The working directory, the environment and the standard streams pass through unchanged.
    /// </summary>
    internal static class SandboxLauncher
    {
        /// <summary>The exit code for a launch that failed before the program ran, as <c>env</c> and container tools use it.</summary>
        public const int LaunchFailed = 125;

        public static int Run(string[] args)
        {
            if (args.Length < 4 || args[0] != ProcessLauncher.PolicyArgument || args[2] != ProcessLauncher.ProgramSeparator)
            {
                Console.Error.WriteLine($"Usage: AI.Sandbox {ProcessLauncher.PolicyArgument} <policy json> {ProcessLauncher.ProgramSeparator} <program> [arguments]");
                return LaunchFailed;
            }

            try
            {
                var policy = SandboxPolicy.FromJson(args[1]);
                var executable = args[3];
                var arguments = args[4..];
                if (OperatingSystem.IsWindows())
                {
                    return AppContainer.Run(policy, executable, arguments);
                }

                if (OperatingSystem.IsLinux())
                {
                    return Landlock.Exec(policy, executable, arguments);
                }

                Console.Error.WriteLine("ai-sandbox: this launcher is used on Windows and Linux only.");
                return LaunchFailed;
            }
            catch (Exception error) when (error is Win32Exception or JsonException or SandboxUnavailableException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Console.Error.WriteLine($"ai-sandbox: {error.Message}");
                return LaunchFailed;
            }
        }
    }
}
