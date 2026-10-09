namespace Build.Targets;

using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

/// <summary>
/// Turns a Desktop start into comparable numbers. Each run gets its own copy of the data
/// directory, the process is asked for the readiness marker the application writes to standard
/// output, and the three phases of a start are read from that line. Numbers come from the marker
/// and nowhere else: a run without the line is a failure, never a silently missing measurement.
/// </summary>
internal sealed class DesktopStartupMeasurementTarget(IProcessRunner processRunner, IBuildPaths buildPaths)
    : IDesktopStartupMeasurementTarget
{
    private const string MarkerPrefix = "Startup: desktop ready mode=";
    private const string EmbeddedAddress = "http://127.0.0.1:52173/";
    private const string SharedAddress = "http://127.0.0.1:52174/";
    private static readonly string[] SourceRootFiles =
        [".lock", "settings.json", "client-settings.json", "browser-access.txt", "theme.json", "window.json", "workspace-location.json"];
    private static readonly TimeSpan MarkerTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan HostStartTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(20);
    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true };

    public async Task<int> RunAsync(
        string scenario,
        int runs,
        string? dataDirectory,
        bool keepCopies,
        CancellationToken cancellationToken)
    {
        if (scenario is not ("embedded" or "shared"))
        {
            await Console.Error.WriteLineAsync($"Unknown scenario '{scenario}'. Use embedded or shared.");
            return 1;
        }

        if (runs < 1)
        {
            await Console.Error.WriteLineAsync("At least one measured run is required.");
            return 1;
        }

        var root = buildPaths.SolutionDirectory;
        var traceDirectory = Path.Combine(root, ".trace");
        Directory.CreateDirectory(traceDirectory);

        var source = ResolveSourceDirectory(dataDirectory);
        if (!Directory.Exists(source))
        {
            await Console.Error.WriteLineAsync($"The data directory to copy does not exist: {source}");
            return 1;
        }

        var copyRoot = ResolveCopyRoot(root);
        Directory.CreateDirectory(copyRoot);

        // One measurement at a time on the machine, not per worktree: the lock sits next to the
        // copies every teammate shares, so no caller can build and run a measurement in parallel
        // with another one and spoil both sets of numbers.
        using var measurementLock = TryAcquireLock(Path.Combine(copyRoot, "measure-desktop.lock"));
        if (measurementLock is null)
        {
            await Console.Error.WriteLineAsync(
                $"Another desktop measurement is already running (lock: {Path.Combine(copyRoot, "measure-desktop.lock")}).");
            return 1;
        }

        var published = Path.Combine(root, "artifacts", "profile-desktop");
        var publishDesktop = await processRunner.RunAsync("Publish desktop for measurement", "dotnet",
            ["publish", "src/AI.Desktop/AI.Desktop.csproj", "--nologo", "-c", "Release", "--output", published],
            cancellationToken);
        if (publishDesktop != 0)
        {
            return publishDesktop;
        }

        var executable = Path.Combine(published, OperatingSystem.IsWindows() ? "AI.Desktop.exe" : "AI.Desktop");
        if (!File.Exists(executable))
        {
            await Console.Error.WriteLineAsync($"Desktop executable was not produced at {executable}.");
            return 1;
        }

        var hostOutput = buildPaths.HostOutputPath;
        var hostExecutable = Path.Combine(hostOutput, OperatingSystem.IsWindows() ? "AI.Host.exe" : "AI.Host");
        if (scenario == "shared")
        {
            var publishHost = await processRunner.RunAsync("Publish host for measurement", "dotnet",
                ["publish", "src/AI.Host/AI.Host.csproj", "--nologo", "-c", "Release", "--output", hostOutput],
                cancellationToken);
            if (publishHost != 0)
            {
                return publishHost;
            }

            if (!File.Exists(hostExecutable))
            {
                await Console.Error.WriteLineAsync($"Host executable was not produced at {hostExecutable}.");
                return 1;
            }
        }
        else if (await IsAnsweringAsync(EmbeddedAddress, cancellationToken))
        {
            // Desktop looks for a shared host on this fixed address. If something answers there, the
            // run would reuse it instead of starting an embedded server and the numbers would
            // describe a different scenario than the one asked for.
            await Console.Error.WriteLineAsync(
                $"Something answers on {EmbeddedAddress}: the embedded scenario needs that address free. Stop the running Host or measure the shared scenario.");
            return 1;
        }

        var runDirectory = Path.Combine(traceDirectory,
            $"measure-desktop-{DateTime.Now:yyyyMMdd-HHmmss}-{scenario}");
        Directory.CreateDirectory(runDirectory);

        var head = await GitHeadAsync(root, cancellationToken);
        Console.WriteLine($"Scenario: {scenario}");
        Console.WriteLine($"Source data directory (read only): {source}");
        Console.WriteLine($"Copies: {copyRoot}");
        Console.WriteLine($"Desktop: {executable}");
        Console.WriteLine($"HEAD: {head ?? "(unknown)"}");
        Console.WriteLine($"Runs: 1 warm-up + {runs} measured");

        var fingerprintBefore = Fingerprint(source);
        var results = new List<RunResult>();
        for (var index = 0; index <= runs; index++)
        {
            var warmup = index == 0;
            var copy = Path.Combine(copyRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}-{scenario}-run{index}");
            if (Directory.Exists(copy))
            {
                Directory.Delete(copy, recursive: true);
            }

            Console.WriteLine($"{(warmup ? "Warm-up" : $"Run {index}")}: copying data to {copy}");
            await CopyDirectoryAsync(source, copy, cancellationToken);

            var result = await RunOnceAsync(scenario, index, executable, published, copy, hostExecutable, hostOutput,
                runDirectory, warmup, cancellationToken);
            results.Add(result);

            if (!keepCopies)
            {
                TryDelete(copy);
            }
        }

        var fingerprintAfter = Fingerprint(source);
        var sourceUnchanged = fingerprintBefore.SequenceEqual(fingerprintAfter);

        var report = new MeasurementReport(
            DateTimeOffset.Now,
            scenario,
            head,
            "Release",
            Path.GetRelativePath(root, published),
            source,
            sourceUnchanged,
            fingerprintBefore,
            fingerprintAfter,
            copyRoot,
            results,
            Summarize(results));
        var reportPath = Path.Combine(runDirectory, "result.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, ReportJson), cancellationToken);

        PrintSummary(scenario, source, sourceUnchanged, results, report.Summary);
        Console.WriteLine($"Report: {Path.GetRelativePath(root, reportPath)}");
        if (!keepCopies)
        {
            Console.WriteLine("Per-run copies were deleted; pass --keep-copies to keep them.");
        }

        var failure = results.FirstOrDefault(result => result.Error is not null);
        if (failure is not null)
        {
            await Console.Error.WriteLineAsync(
                $"Run {failure.Index} failed: {failure.Error}");
            return 1;
        }

        if (!sourceUnchanged)
        {
            await Console.Error.WriteLineAsync("The source data directory changed during the measurement; the numbers are not trustworthy.");
            return 1;
        }

        return 0;
    }

    private static async Task<RunResult> RunOnceAsync(
        string scenario,
        int index,
        string executable,
        string published,
        string copy,
        string hostExecutable,
        string hostOutput,
        string runDirectory,
        bool warmup,
        CancellationToken cancellationToken)
    {
        Process? host = null;
        try
        {
            if (scenario == "shared")
            {
                host = StartHost(hostExecutable, hostOutput, copy, Path.Combine(runDirectory, $"run{index}-host.log"));
                if (host is null)
                {
                    return Failed(index, warmup, copy, "the Host process could not be started");
                }

                if (!await WaitForHostAsync(host, cancellationToken))
                {
                    var detail = host.HasExited ? $"the Host exited with code {host.ExitCode}" : "the Host did not answer in time";
                    StopVerified(host);
                    return Failed(index, warmup, copy, detail);
                }
            }

            var startInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = published,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            // The copy is the whole world of this run: the application never touches the original.
            startInfo.Environment["AI_CLIENT_DATA_DIRECTORY"] = copy;
            startInfo.ArgumentList.Add("--data-dir");
            startInfo.ArgumentList.Add(copy);
            if (scenario == "shared")
            {
                // Desktop looks for the shared host on this address; without the override it would
                // ask the fixed one, which a running installation may occupy.
                startInfo.Environment["AI_CLIENT_HOST_ADDRESS"] = SharedAddress;
            }

            var duration = Stopwatch.StartNew();
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                StopVerified(host);
                return Failed(index, warmup, copy, "the Desktop process could not be started");
            }

            var pid = process.Id;
            var started = process.StartTime;
            var stdoutLog = Path.Combine(runDirectory, $"run{index}-stdout.log");
            var markerTask = WatchMarkerAsync(process.StandardOutput, stdoutLog);
            var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

            var marker = await WaitForMarkerAsync(markerTask, process, cancellationToken);
            duration.Stop();

            var closedByPid = await StopAsync(process, pid, started);
            var error = await errorTask;
            StopVerified(host);

            if (marker is null)
            {
                var detail = process.HasExited
                    ? $"no readiness marker was written (exit code {process.ExitCode}); standard error: {Tail(error)}"
                    : "no readiness marker was written before the timeout";
                return Failed(index, warmup, copy, detail);
            }

            if (!TryParseMarker(marker, out var mode, out var server, out var ui, out var desktop))
            {
                return Failed(index, warmup, copy, $"the readiness marker has an unexpected format: {marker}");
            }

            if (mode == "failed")
            {
                return Failed(index, warmup, copy, "the application reported mode=failed: its server did not start");
            }

            if (mode != scenario)
            {
                return Failed(index, warmup, copy,
                    $"the application reported mode={mode} while the {scenario} scenario was requested" +
                    (mode == "embedded" && scenario == "shared"
                        ? "; the running build may not honour AI_CLIENT_HOST_ADDRESS yet"
                        : string.Empty));
            }

            if (server is null || ui is null || desktop is null)
            {
                return Failed(index, warmup, copy, $"the readiness marker is missing a phase: {marker}");
            }

            var result = new RunResult(index, warmup, pid, copy, mode, server, ui, desktop,
                Math.Round(duration.Elapsed.TotalMilliseconds), closedByPid, null);
            Console.WriteLine(
                $"  {(warmup ? "warm-up" : "measured")} pid={pid} mode={mode} server={Format(server)} ui={Format(ui)} desktop={Format(desktop)} total={result.TotalMilliseconds} ms" +
                (closedByPid ? string.Empty : " (terminated by PID)"));
            return result;
        }
        catch (OperationCanceledException)
        {
            StopVerified(host);
            throw;
        }
#pragma warning disable CA1031 // A failed run is a recorded result, not a crash of the measurement.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            StopVerified(host);
            return Failed(index, warmup, copy, exception.Message);
        }
    }

    private static Process? StartHost(string hostExecutable, string hostOutput, string copy, string logPath)
    {
        var startInfo = new ProcessStartInfo(hostExecutable)
        {
            WorkingDirectory = hostOutput,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // The temporary host owns the same copy as the Desktop app, so the shared scenario has one
        // data directory and one server, exactly like an installed host.
        startInfo.Environment["AI_CLIENT_DATA_DIRECTORY"] = copy;
        startInfo.ArgumentList.Add("--data-dir");
        startInfo.ArgumentList.Add(copy);
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(SharedAddress.TrimEnd('/'));
        // The desktop app only recognises a host that answers the bridge session endpoint, and
        // that endpoint exists in public-web mode only; it also serves the local Desktop UI, which
        // the app checks next. Without a window it needs no tray icon.
        startInfo.ArgumentList.Add("--public-web");
        startInfo.ArgumentList.Add("--no-tray");
        var host = Process.Start(startInfo);
        if (host is not null)
        {
            // The pipes must be drained or a chatty host would block on a full one; the log keeps
            // the reason a host never answered visible after the run.
            var log = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };
            _ = DrainAsync(host.StandardOutput, log, "stdout");
            _ = DrainAsync(host.StandardError, log, "stderr");
        }

        return host;
    }

    private static async Task DrainAsync(StreamReader reader, StreamWriter log, string stream)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                lock (log)
                {
                    log.WriteLine($"[{stream}] {line}");
                }
            }
        }
        catch (Exception)
        {
            // A closed pipe at the end of the host's life is nothing to report.
        }
    }

    private static async Task<bool> WaitForHostAsync(Process host, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < HostStartTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (host.HasExited)
            {
                return false;
            }

            if (await IsAnsweringAsync(SharedAddress + "api/bridge/session", cancellationToken))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// Stops a measured process: the window is asked to close first, and only a process still
    /// running afterwards is killed — by the id it was started with, after checking that the id
    /// still names the same process. Killing by process name would end an unrelated installation.
    /// </summary>
    private static async Task<bool> StopAsync(Process process, int pid, DateTime started)
    {
        try
        {
            if (process.HasExited)
            {
                return true;
            }

            var closed = process.CloseMainWindow();
            if (closed)
            {
                using var timeout = new CancellationTokenSource(ShutdownTimeout);
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    // The window did not close in time; fall through to the verified kill.
                }
            }

            KillVerified(pid, started);
            using var forced = new CancellationTokenSource(ShutdownTimeout);
            try
            {
                await process.WaitForExitAsync(forced.Token);
            }
            catch (OperationCanceledException)
            {
                // Nothing left to try: the run is recorded as terminated by PID either way.
            }

            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void StopVerified(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (process.HasExited)
            {
                return;
            }

            KillVerified(process.Id, process.StartTime);
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    private static void KillVerified(int pid, DateTime expectedStart)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            // The id is reused by the operating system; the start time proves this is our process.
            if (process.StartTime != expectedStart)
            {
                return;
            }

            process.Kill(entireProcessTree: false);
        }
        catch (ArgumentException)
        {
            // No process with that id any more.
        }
        catch (InvalidOperationException)
        {
            // It exited between the lookup and the kill.
        }
    }

    private static async Task<string?> WaitForMarkerAsync(Task<string?> marker, Process process, CancellationToken cancellationToken)
    {
        var finished = await Task.WhenAny(marker, Task.Delay(MarkerTimeout, cancellationToken));
        if (finished == marker)
        {
            return await marker;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    private static Task<string?> WatchMarkerAsync(StreamReader reader, string logPath)
    {
        var found = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () =>
        {
            try
            {
                await using var log = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };
                while (await reader.ReadLineAsync() is { } line)
                {
                    await log.WriteLineAsync(line);
                    if (line.StartsWith(MarkerPrefix, StringComparison.Ordinal))
                    {
                        found.TrySetResult(line);
                    }
                }
            }
            catch (Exception)
            {
                // A closed pipe while the process dies is the normal end of this loop.
            }

            found.TrySetResult(null);
        });
        return found.Task;
    }

    private static bool TryParseMarker(string line, out string mode, out double? server, out double? ui, out double? desktop)
    {
        mode = string.Empty;
        server = ui = desktop = null;
        var fields = line[MarkerPrefix.Length..].Split('\t');
        if (fields.Length != 4)
        {
            return false;
        }

        mode = fields[0];
        if (mode.Length == 0)
        {
            return false;
        }

        if (!TryParsePhase(fields[1], out server) || !TryParsePhase(fields[2], out ui) || !TryParsePhase(fields[3], out desktop))
        {
            mode = string.Empty;
            return false;
        }

        return true;
    }

    private static bool TryParsePhase(string field, out double? value)
    {
        value = null;
        if (field == "-")
        {
            return true;
        }

        if (double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static PhaseSummary? Summarize(IReadOnlyList<RunResult> results, Func<RunResult, double?> select)
    {
        var values = results.Where(result => !result.Warmup).Select(select)
            .Where(value => value is not null).Select(value => value!.Value).Order().ToArray();
        if (values.Length == 0)
        {
            return null;
        }

        return new PhaseSummary(Median(values), values[0], values[^1], values.Length);
    }

    private static MeasurementSummary Summarize(IReadOnlyList<RunResult> results) => new(
        Summarize(results, result => result.ServerReady),
        Summarize(results, result => result.UiReady),
        Summarize(results, result => result.DesktopReady));

    private static double Median(double[] sorted) => sorted.Length % 2 == 1
        ? sorted[sorted.Length / 2]
        : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;

    private static void PrintSummary(
        string scenario,
        string source,
        bool sourceUnchanged,
        IReadOnlyList<RunResult> results,
        MeasurementSummary summary)
    {
        Console.WriteLine();
        Console.WriteLine($"Desktop startup measurement — scenario {scenario}");
        Console.WriteLine($"  source data directory: {source}");
        Console.WriteLine($"  source unchanged: {(sourceUnchanged ? "yes" : "NO — the numbers are not trustworthy")}");
        Console.WriteLine($"  warm-up runs (not counted): {results.Count(result => result.Warmup)}");
        Console.WriteLine($"  measured runs (counted): {results.Count(result => !result.Warmup)}");
        Console.WriteLine("  phase            median      min        max   runs");
        PrintPhase("  serverReady ", summary.ServerReady);
        PrintPhase("  uiReady     ", summary.UiReady);
        PrintPhase("  desktopReady", summary.DesktopReady);
    }

    private static void PrintPhase(string label, PhaseSummary? summary) =>
        Console.WriteLine(summary is null
            ? $"{label}         (no value)"
            : $"{label}  {summary.Median,9:F0} {summary.Min,9:F0} {summary.Max,9:F0}  {summary.Runs,4}");

    private static string Format(double? value) =>
        value?.ToString("F0", CultureInfo.InvariantCulture) ?? "-";

    private static string Tail(string text)
    {
        const int limit = 600;
        var trimmed = text.Trim();
        return trimmed.Length <= limit ? trimmed : trimmed[^limit..];
    }

    private static RunResult Failed(int index, bool warmup, string copy, string error) => new(
        index, warmup, null, copy, null, null, null, null, null, null, error);

    private static string ResolveSourceDirectory(string? dataDirectory)
    {
        var source = dataDirectory;
        if (string.IsNullOrWhiteSpace(source))
        {
            source = Environment.GetEnvironmentVariable("AI_CLIENT_DATA_DIRECTORY");
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI");
        }

        return Path.GetFullPath(source);
    }

    private static string ResolveCopyRoot(string root)
    {
        // The copies are gigabytes of user data: they belong on a scratch disk, never next to the
        // repository. AI_CLIENT_MEASUREMENT_ROOT overrides the location.
        var copyRoot = Environment.GetEnvironmentVariable("AI_CLIENT_MEASUREMENT_ROOT");
        if (string.IsNullOrWhiteSpace(copyRoot))
        {
            copyRoot = Path.Combine("D:\\AT_tests", "desktop-startup");
        }

        copyRoot = Path.GetFullPath(copyRoot);
        var solution = Path.GetFullPath(root);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (copyRoot.Equals(solution, comparison) ||
            copyRoot.StartsWith(solution + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException(
                $"The measurement copies must live outside the repository; '{copyRoot}' is inside '{solution}'. " +
                "Set AI_CLIENT_MEASUREMENT_ROOT to a scratch directory outside the repository.");
        }

        return copyRoot;
    }

    private static FileStream? TryAcquireLock(string path)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sizes and timestamps of the source's root files: comparing the snapshot before and after the
    /// runs is the evidence that the measurement never wrote into the data directory it copies.
    /// </summary>
    private static string[] Fingerprint(string directory) => SourceRootFiles.Select(name =>
    {
        var info = new FileInfo(Path.Combine(directory, name));
        return info.Exists
            ? $"{name}={info.Length}@{info.LastWriteTimeUtc:O}"
            : $"{name}=missing";
    }).ToArray();

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToArray();
        await Parallel.ForEachAsync(files,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken },
            (file, _) =>
            {
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
                return ValueTask.CompletedTask;
            });
    }

    private static void TryDelete(string directory)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
        }
    }

    private static async Task<bool> IsAnsweringAsync(string address, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await client.GetAsync(address, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    private static async Task<string?> GitHeadAsync(string root, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("rev-parse");
        startInfo.ArgumentList.Add("HEAD");
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0 ? output.Trim() : null;
    }

    private sealed record RunResult(
        int Index,
        bool Warmup,
        int? Pid,
        string CopyDirectory,
        string? Mode,
        double? ServerReady,
        double? UiReady,
        double? DesktopReady,
        double? TotalMilliseconds,
        bool? ClosedGracefully,
        string? Error);

    private sealed record PhaseSummary(double Median, double Min, double Max, int Runs);

    private sealed record MeasurementSummary(PhaseSummary? ServerReady, PhaseSummary? UiReady, PhaseSummary? DesktopReady);

    private sealed record MeasurementReport(
        DateTimeOffset StartedAt,
        string Scenario,
        string? Head,
        string Configuration,
        string DesktopArtifact,
        string SourceDataDirectory,
        bool SourceUnchanged,
        IReadOnlyList<string> SourceBefore,
        IReadOnlyList<string> SourceAfter,
        string CopyRoot,
        IReadOnlyList<RunResult> Runs,
        MeasurementSummary Summary);
}
