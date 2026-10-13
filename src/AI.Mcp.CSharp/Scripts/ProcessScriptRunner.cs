namespace AI.Mcp.CSharp.Scripts;

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AI.Contracts.Processes;

/// <summary>
/// Runs each script in a disposable process so a blocking script cannot hold the MCP server past
/// its deadline. The worker's standard output carries one serialized result, not script output.
/// </summary>
public sealed class ProcessScriptRunner : IScriptRunner
{
    private const int MaxResultCharacters = 4 * 1024 * 1024;
    private const int MaxErrorCharacters = 64 * 1024;

    public async Task<ScriptResult> RunAsync(ScriptRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();
        if (request.TimeoutMs is < 1 or > ScriptLimits.MaxTimeoutMs)
        {
            return Failure(stopwatch, $"Timeout must be between 1 and {ScriptLimits.MaxTimeoutMs} milliseconds.");
        }

        using var job = new ChildProcessJob(request.Limits ?? ChildProcessLimits.Default);
        var result = await RunWorkerAsync(request, job, stopwatch, cancellationToken);
        return result with { LimitExceeded = LimitExceeded(job, result) };
    }

    /// <summary>
    /// The limit the run ran into. The job names it when it refused an allocation or a process, but
    /// the worker is a .NET process that sizes its own heap from the job's memory limit, so it usually
    /// throws <see cref="OutOfMemoryException"/> before the job has anything to refuse; under a memory
    /// limit that exception is the limit.
    /// </summary>
    private static string? LimitExceeded(ChildProcessJob job, ScriptResult result) =>
        job.LimitExceeded
        ?? (OperatingSystem.IsWindows() && result.Error?.StartsWith(nameof(OutOfMemoryException), StringComparison.Ordinal) == true
            ? ChildProcessJob.Memory
            : null);

    private static async Task<ScriptResult> RunWorkerAsync(
        ScriptRequest request, ChildProcessJob job, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.TimeoutMs);

        using var process = new Process { StartInfo = WorkerStartInfo() };
        var started = false;
        try
        {
            process.Start();
            started = true;
            // A worker that cannot be placed is killed below: an unlimited run is not what was asked for.
            job.Attach(process);
            var stdout = ReadBoundedAsync(process.StandardOutput, MaxResultCharacters, deadline.Token);
            var stderr = ReadBoundedAsync(process.StandardError, MaxErrorCharacters, deadline.Token);

            await JsonSerializer.SerializeAsync(process.StandardInput.BaseStream, request, cancellationToken: deadline.Token);
            await process.StandardInput.FlushAsync(deadline.Token);
            process.StandardInput.Close();

            var response = await stdout;
            var errors = await stderr;
            await process.WaitForExitAsync(deadline.Token);
            if (process.ExitCode != 0)
            {
                return Failure(stopwatch, $"Script worker exited with code {process.ExitCode}: {errors.Text}");
            }

            if (response.Truncated)
            {
                return Failure(stopwatch, "Script worker result exceeded the output limit.");
            }

            return JsonSerializer.Deserialize<ScriptResult>(response.Text)
                   ?? Failure(stopwatch, "Script worker returned no result.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var message = $"Script exceeded the {request.TimeoutMs} ms timeout.";
            return new ScriptResult(false, null, null, [], "", "",
                [new ScriptDiagnostic("Error", "CSX0001", message, 0, 0)],
                stopwatch.ElapsedMilliseconds, true, false, "Script timed out.");
        }
        catch (Exception error) when (error is Win32Exception or IOException or JsonException or InvalidOperationException)
        {
            return Failure(stopwatch, $"Script worker failed: {error.Message}");
        }
        finally
        {
            if (started && !process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception)
                {
                    // The process exited during cleanup, or the OS refused termination.
                }
            }
        }
    }

    private static ProcessStartInfo WorkerStartInfo()
    {
        var executable = Environment.ProcessPath
                         ?? throw new InvalidOperationException("Cannot locate the script worker executable.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(typeof(ScriptRunner).Assembly.Location);
        }

        start.ArgumentList.Add("--script-worker");
        return start;
    }

    private static async Task<(string Text, bool Truncated)> ReadBoundedAsync(
        StreamReader reader, int limit, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        var truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            var keep = Math.Min(count, limit - text.Length);
            text.Append(buffer, 0, keep);
            truncated |= keep != count;
        }

        return (text.ToString(), truncated);
    }

    private static ScriptResult Failure(Stopwatch stopwatch, string message) =>
        new(false, null, null, [], "", "", [], stopwatch.ElapsedMilliseconds, false, false, message);
}
