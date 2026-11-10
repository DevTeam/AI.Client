namespace AI.Search.Benchmarks;

using System.Diagnostics;

/// <summary>
/// Runs the real <c>rg</c> binary as the comparison baseline. The process is started per run and
/// its whole lifetime is measured, so the numbers include what a command-line tool has to pay —
/// startup and argument parsing — rather than only the search it performs.
/// </summary>
/// <remarks>
/// The binary is looked up in the order a person would: <c>--rg</c>, then <c>AI_SEARCH_RG</c>, then
/// <c>PATH</c>, then the portable copy this machine keeps outside <c>PATH</c>. A missing binary is
/// reported as such, so a run can still measure the engine alone.
/// </remarks>
internal sealed record RipgrepRunner(string Executable)
{
    /// <summary>Where the portable copy on this machine lives, used when nothing else is found.</summary>
    public const string FallbackPath = @"D:\App\MCP\ripgrep\ripgrep-15.2.0-x86_64-pc-windows-msvc\rg.exe";

    /// <summary>Finds the binary, or null when there is none to find.</summary>
    public static RipgrepRunner? Locate(string? explicitPath)
    {
        string?[] candidates = [explicitPath, Environment.GetEnvironmentVariable("AI_SEARCH_RG"), FallbackPath];
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return new RipgrepRunner(candidate);
            }
        }

        var names = OperatingSystem.IsWindows() ? new[] { "rg.exe", "rg" } : ["rg"];
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                var candidate = System.IO.Path.Combine(directory.Trim('"'), name);
                if (File.Exists(candidate))
                {
                    return new RipgrepRunner(candidate);
                }
            }
        }

        return null;
    }

    /// <summary>The version the numbers belong to, which belongs in any report of them.</summary>
    public async Task<string> VersionAsync()
    {
        var output = await RunAsync(["--version"], TimeSpan.FromSeconds(30), CancellationToken.None).ConfigureAwait(false);
        return output.StandardOutput.Split('\n')[0].Trim();
    }

    /// <summary>
    /// One workload: an untimed run to warm the tree into the operating system's cache, then
    /// <paramref name="runs"/> timed ones, of which the median is reported.
    /// </summary>
    public async Task<ComparisonSample> MeasureAsync(SearchWorkload workload, int runs, CancellationToken cancellationToken)
    {
        await RunAsync(workload.RipgrepArgs, TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
        var samples = new List<double>(runs);
        var matches = 0;
        for (var index = 0; index < runs; index++)
        {
            var output = await RunAsync(workload.RipgrepArgs, TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
            samples.Add(output.Elapsed.TotalMilliseconds);
            matches = CountLines(output.StandardOutput);
            if (output.ExitCode > 1)
            {
                throw new InvalidOperationException($"ripgrep failed: {output.StandardError}");
            }
        }

        return ComparisonSample.Create(samples, matches);
    }

    /// <summary>Reported lines, which is what one match per line plus a file name makes.</summary>
    private static int CountLines(string output)
    {
        var count = 0;
        foreach (var line in output.Split('\n'))
        {
            if (line.Trim().Length > 0)
            {
                count++;
            }
        }

        return count;
    }

    private async Task<RipgrepOutput> RunAsync(
        IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(Executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException("The ripgrep process could not be started.");
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancelled.CancelAfter(timeout);
        var startedAt = Stopwatch.GetTimestamp();
        var output = await process.StandardOutput.ReadToEndAsync(cancelled.Token).ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync(cancelled.Token).ConfigureAwait(false);
        await process.WaitForExitAsync(cancelled.Token).ConfigureAwait(false);
        return new RipgrepOutput(output, error, process.ExitCode, Stopwatch.GetElapsedTime(startedAt));
    }

    private sealed record RipgrepOutput(string StandardOutput, string StandardError, int ExitCode, TimeSpan Elapsed);
}