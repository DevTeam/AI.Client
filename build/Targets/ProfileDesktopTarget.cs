namespace Build.Targets;

using System.Diagnostics;
using System.Text;
using System.Text.Json;

internal sealed class ProfileDesktopTarget(IProcessRunner processRunner, IBuildPaths buildPaths) : IProfileDesktopTarget
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new() { WriteIndented = true };

    public async Task<int> RunAsync(string profiler, string? dataDirectory, CancellationToken cancellationToken)
    {
        var root = buildPaths.SolutionDirectory;
        var published = Path.Combine(root, "artifacts", "profile-desktop");
        var publish = await processRunner.RunAsync("Publish desktop for profiling", "dotnet",
            ["publish", "src/AI.Desktop/AI.Desktop.csproj", "--nologo", "-c", "Release",
                "--output", published], cancellationToken);
        if (publish != 0) return publish;

        var executable = Path.Combine(published, OperatingSystem.IsWindows() ? "AI.Desktop.exe" : "AI.Desktop");
        if (!File.Exists(executable))
        {
            await Console.Error.WriteLineAsync($"Desktop executable was not produced at {executable}.");
            return 1;
        }

        var started = DateTimeOffset.Now;
        var runDirectory = Path.Combine(root, ".trace", $"desktop-{started:yyyyMMdd-HHmmss}-{profiler}");
        Directory.CreateDirectory(runDirectory);
        var metadata = new
        {
            startedAt = started,
            profiler,
            executable,
            dataDirectory,
            instructions = "Close the Desktop window to finish and flush the trace."
        };
        await File.WriteAllTextAsync(Path.Combine(runDirectory, "session.json"),
            JsonSerializer.Serialize(metadata, MetadataJsonOptions), cancellationToken);

        var isDotTrace = profiler.StartsWith("dottrace", StringComparison.Ordinal);
        var arguments = new List<string> { "tool", "run", isDotTrace ? "dottrace" : "dotnet-trace", "--" };
        if (isDotTrace)
        {
            var timeline = profiler == "dottrace-timeline";
            arguments.AddRange(["start", $"--profiling-type={(timeline ? "Timeline" : "Sampling")}",
                $"--save-to={Path.Combine(runDirectory, timeline ? "desktop.dtt" : "desktop.dtp")}",
                $"--work-dir={published}", "--no-check-for-updates"]);
            if (timeline) arguments.Add("--ask-uac-elevation");
            arguments.Add(executable);
        }
        else
        {
            arguments.AddRange(["collect", "--format", "Speedscope", "--output",
                Path.Combine(runDirectory, "desktop.nettrace"), "--", executable]);
        }
        if (!string.IsNullOrWhiteSpace(dataDirectory))
        {
            if (isDotTrace) arguments.Add("--");
            arguments.Add("--data-dir");
            arguments.Add(Path.GetFullPath(dataDirectory));
        }

        Console.WriteLine($"Profile directory: {runDirectory}");
        Console.WriteLine("Close the Desktop window after exercising startup and chat/branch switching.");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = isDotTrace ? root : published,
            UseShellExecute = false,
            RedirectStandardInput = profiler == "eventpipe"
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the profiler.");
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (profiler == "eventpipe" && !process.HasExited)
        {
            // dotnet-trace finalizes its trace when it receives Enter.
            await process.StandardInput.WriteLineAsync();
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (OperationCanceledException) when (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            return 1;
        }
        if (process.ExitCode != 0) return process.ExitCode;

        if (profiler == "eventpipe")
        {
            var trace = Path.Combine(runDirectory, "desktop.nettrace");
            if (!File.Exists(trace))
            {
                await Console.Error.WriteLineAsync($"No trace was saved at {trace}.");
                return 1;
            }
            var speedscope = Path.Combine(runDirectory, "desktop.speedscope.json");
            if (!File.Exists(speedscope))
            {
                await Console.Error.WriteLineAsync($"No Speedscope JSON was saved at {speedscope}.");
                return 1;
            }
            await WriteReportAsync(trace, Path.Combine(runDirectory, "top-exclusive.txt"), false);
            await WriteReportAsync(trace, Path.Combine(runDirectory, "top-inclusive.txt"), true);
        }
        else
        {
            var snapshot = Path.Combine(runDirectory, profiler == "dottrace" ? "desktop.dtp" : "desktop.dtt");
            if (!File.Exists(snapshot))
            {
                await Console.Error.WriteLineAsync($"No dotTrace snapshot was saved at {snapshot}.");
                return 1;
            }
        }

        Console.WriteLine($"Profile saved to {runDirectory}");
        return 0;

        async Task WriteReportAsync(string trace, string output, bool inclusive)
        {
            var reportInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[] { "tool", "run", "dotnet-trace", "--", "report", trace, "topN", "-n", "100" })
                reportInfo.ArgumentList.Add(argument);
            if (inclusive) reportInfo.ArgumentList.Add("--inclusive");
            using var report = Process.Start(reportInfo)
                ?? throw new InvalidOperationException("Could not start dotnet-trace report.");
            var stdout = report.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderr = report.StandardError.ReadToEndAsync(CancellationToken.None);
            await report.WaitForExitAsync(CancellationToken.None);
            var content = await stdout;
            var error = await stderr;
            if (report.ExitCode != 0)
                throw new InvalidOperationException($"dotnet-trace report failed: {error}");
            await File.WriteAllTextAsync(output, content, new UTF8Encoding(false), CancellationToken.None);
        }
    }
}
