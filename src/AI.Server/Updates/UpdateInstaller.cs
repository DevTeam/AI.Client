namespace AI.Updates;

using AI.Contracts.Updates;
using System.Diagnostics;
using System.Text.Json;

public sealed class UpdateInstaller : IUpdateInstaller
{
    public async Task LaunchAsync(UpdateState state, string directory, string package, string result, CancellationToken token)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The application executable could not be located.");
        var installDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var plan = new InstallationPlan(Environment.ProcessId, state.Product, state.Release!.Version, package,
            state.Release.Sha256, result, executable, installDirectory, Environment.GetCommandLineArgs().Skip(1).ToArray(),
            Directory.Exists(Path.Combine(installDirectory, "mcp-csharp")),
            OperatingSystem.IsLinux() && Directory.Exists("/opt/ai-client-csharp-mcp") ? Path.Combine(directory, "companion.deb") : null,
            state.Release.CompanionSha256);
        var script = Path.Combine(directory, OperatingSystem.IsWindows() ? "install.ps1" : "install.sh");
        var resource = "AI.Updates." + Path.GetFileName(script);
        await using (var stream = typeof(UpdateInstaller).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("The update helper is missing."))
        await using (var file = File.Create(script))
            await stream.CopyToAsync(file, token);

        ProcessStartInfo start;
        if (OperatingSystem.IsWindows())
        {
            var planPath = Path.Combine(directory, "plan.json");
            await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan), token);
            start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-PlanPath", planPath }) start.ArgumentList.Add(argument);
        }
        else
        {
            // A separate service owns the helper, so stopping the Host's service cannot kill it.
            var separateService = state.Product == "Host";
            start = new ProcessStartInfo(separateService ? OperatingSystem.IsMacOS() ? "/bin/launchctl" : "systemd-run" : "/usr/bin/nohup") { UseShellExecute = false };
            if (separateService)
            {
                var arguments = OperatingSystem.IsMacOS()
                    ? new[] { "submit", "-l", $"org.devteam.ai-client-update.{Environment.ProcessId}", "--" }
                    : new[] { "--user", $"--unit=ai-client-update-{Environment.ProcessId}", "--collect" };
                foreach (var argument in arguments) start.ArgumentList.Add(argument);
            }
            start.ArgumentList.Add("/bin/sh");
            foreach (var argument in new[] { script, plan.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                plan.Product, plan.Version, plan.Package, plan.Sha256, plan.Result, plan.Executable,
                plan.InstallDirectory, plan.Companion ?? "", plan.CompanionSha256 ?? "" }) start.ArgumentList.Add(argument);
            foreach (var argument in plan.Arguments) start.ArgumentList.Add(argument);
        }
        using var helper = Process.Start(start) ?? throw new InvalidOperationException("The update helper did not start.");
        // Catch an immediate launch failure before asking the application to exit.
        await Task.Delay(200, token);
        if (helper.HasExited && (OperatingSystem.IsWindows() || state.Product != "Host" || helper.ExitCode != 0))
            throw new InvalidOperationException("The update helper exited before installation. See the update log.");
    }

    public sealed record InstallationPlan(int Pid, string Product, string Version, string Package, string Sha256,
        string Result, string Executable, string InstallDirectory, string[] Arguments, bool CSharp,
        string? Companion, string? CompanionSha256);
}
