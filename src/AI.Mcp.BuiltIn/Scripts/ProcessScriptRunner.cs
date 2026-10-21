namespace AI.Mcp.BuiltIn.Scripts;

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AI.Mcp.BuiltIn.Sandbox;
using AI.Sandbox;

/// <summary>
/// Runs each script in a disposable worker process so a blocking script cannot hold the MCP server
/// past its deadline, and so one script cannot leave state behind for the next. The worker's
/// standard output carries one serialized result, not script output. Workers come from
/// <see cref="IScriptWorkers"/>, which keeps the next one started and warmed up.
/// </summary>
public sealed class ProcessScriptRunner(IScriptWorkers workers, IToolSandbox sandbox, IProcessLauncher launcher) : IScriptRunner
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

        var mechanism = sandbox.Policy(SandboxedTool.Scripts) is null ? null : launcher.Support.Mechanism;
        ScriptResult result;
        try
        {
            using var worker = workers.Take(request.Limits ?? ChildProcessLimits.Default);
            result = await ExchangeAsync(worker, request, stopwatch, cancellationToken);
            result = result with { LimitExceeded = LimitExceeded(worker, result) };
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            result = Failure(stopwatch, $"Script worker failed to start: {error.Message}");
        }
        finally
        {
            // The next call should not pay for starting the compiler; this one already did.
            workers.Replenish();
        }

        return result with { Sandbox = mechanism };
    }

    /// <summary>
    /// The limit the run ran into. The job names it when it refused an allocation or a process, but
    /// the worker is a .NET process that sizes its own heap from the job's memory limit, so it usually
    /// throws <see cref="OutOfMemoryException"/> before the job has anything to refuse; under a memory
    /// limit that exception is the limit.
    /// </summary>
    private static string? LimitExceeded(ILaunchedProcess worker, ScriptResult result) =>
        worker.LimitExceeded
        ?? (OperatingSystem.IsWindows() && result.Error?.StartsWith(nameof(OutOfMemoryException), StringComparison.Ordinal) == true
            ? ChildProcessJob.Memory
            : null);

    private static async Task<ScriptResult> ExchangeAsync(
        ILaunchedProcess worker, ScriptRequest request, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.TimeoutMs);
        var process = worker;
        try
        {
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
            worker.Kill();
            var message = $"Script exceeded the {request.TimeoutMs} ms timeout.";
            return new ScriptResult(false, null, null, [], "", "",
                [new ScriptDiagnostic("Error", "CSX0001", message, 0, 0)],
                stopwatch.ElapsedMilliseconds, true, false, "Script timed out.");
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException)
        {
            return Failure(stopwatch, $"Script worker failed: {error.Message}");
        }
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
