namespace AI.Mcp.BuiltIn.Process;

using System.Diagnostics;
using Sandbox;

/// <summary>
/// Owns executions until completion or server shutdown. Call cancellation cancels only that wait;
/// a launch cancelled before returning its handle is stopped so it cannot become an orphan.
/// Full byte logs survive result pagination and session shutdown in the chat's temporary directory.
/// </summary>
public sealed class ProcessSessions(IProcessRunner runner, IToolSandbox sandbox) : IProcessSessions, IAsyncDisposable
{
    public const int DefaultWaitTimeoutMs = 10000;
    public const int MaxWaitTimeoutMs = 60000;
    private const int MaximumSessions = 128;
    private const int MaximumRunning = 16;
    private readonly object _gate = new();
    private readonly Dictionary<string, Execution> _executions = new(StringComparer.Ordinal);
    private bool _disposed;

    public async Task<ProcessResult> RunAsync(ProcessRequest request, int waitTimeoutMs, CancellationToken cancellationToken)
    {
        ValidateWait(waitTimeoutMs);
        cancellationToken.ThrowIfCancellationRequested();
        Execution execution;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_executions.Values.Count(item => !item.Result.IsCompleted) >= MaximumRunning)
                return Failure("Too many running processes in this session. Wait for or stop an existing process.");
            // Keep recent results retryable, without retaining every execution for an unbounded session.
            foreach (var item in _executions.Values.Where(item => item.Result.IsCompleted)
                         .OrderBy(item => item.Started).Take(Math.Max(0, _executions.Count - MaximumSessions + 1)).ToArray())
            {
                _executions.Remove(item.Id);
                item.Stop.Dispose();
            }
            var id = Guid.NewGuid().ToString("N");
            var root = sandbox.TemporaryDirectory ?? Path.GetTempPath();
            var directory = Directory.CreateDirectory(Path.Combine(root, "process-" + id)).FullName;
            execution = new Execution(id, directory);
            _executions.Add(id, execution);
            execution.Result = ExecuteAsync(execution, request);
        }
        try
        {
            return await SnapshotAsync(execution, waitTimeoutMs, 0, 0, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await execution.Stop.CancelAsync();
            await execution.Result;
            throw;
        }
    }

    public Task<ProcessResult> WaitAsync(string processId, int waitTimeoutMs, long stdoutOffset, long stderrOffset, CancellationToken cancellationToken)
    {
        ValidateWait(waitTimeoutMs);
        ArgumentOutOfRangeException.ThrowIfNegative(stdoutOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(stderrOffset);
        var execution = Find(processId);
        return execution is null ? Task.FromResult(Failure("Unknown or expired processId in this MCP session."))
            : SnapshotAsync(execution, waitTimeoutMs, stdoutOffset, stderrOffset, cancellationToken);
    }

    public async Task<ProcessResult> StopAsync(string processId, CancellationToken cancellationToken)
    {
        var execution = Find(processId);
        if (execution is null) return Failure("Unknown or expired processId in this MCP session.");
        lock (_gate)
        {
            if (!execution.Result.IsCompleted) execution.Stop.Cancel();
        }
        await execution.Result.WaitAsync(cancellationToken);
        return await SnapshotAsync(execution, 0, 0, 0, cancellationToken);
    }

    private Execution? Find(string id)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _executions.GetValueOrDefault(id);
        }
    }

    private async Task<ProcessResult> ExecuteAsync(Execution execution, ProcessRequest request)
    {
        try
        {
            await using var stdout = OpenLog(execution.StdoutPath);
            await using var stderr = OpenLog(execution.StderrPath);
            var result = await runner.RunAsync(request, execution.Stop.Token, new ProcessOutput(stdout, stderr));
            return result with { Status = result.TimedOut ? "timed_out" : result.Error is not null ? "failed" : "completed" };
        }
        catch (OperationCanceledException) when (execution.Stop.IsCancellationRequested)
        {
            return new ProcessResult(null, "", "", execution.Timer.ElapsedMilliseconds, false, false,
                "Process stopped before completion; partial effects may have occurred.", Status: "stopped");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return Failure(error.Message) with { DurationMs = execution.Timer.ElapsedMilliseconds };
        }
    }

    private static FileStream OpenLog(string path) => new(path, FileMode.CreateNew, FileAccess.Write,
        FileShare.Read, 4096, FileOptions.Asynchronous);

    private static async Task<ProcessResult> SnapshotAsync(Execution execution, int waitTimeoutMs,
        long stdoutOffset, long stderrOffset, CancellationToken cancellationToken)
    {
        if (!execution.Result.IsCompleted && waitTimeoutMs > 0)
        {
            try { await execution.Result.WaitAsync(TimeSpan.FromMilliseconds(waitTimeoutMs), cancellationToken); }
            catch (TimeoutException) { /* A wait expiring leaves the execution alive. */ }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var result = execution.Result.IsCompleted ? await execution.Result
            : new ProcessResult(null, "", "", execution.Timer.ElapsedMilliseconds, false, false, null, Status: "running");
        var stdout = await ReadLogAsync(execution.StdoutPath, stdoutOffset, result.Status == "running", cancellationToken);
        var stderr = await ReadLogAsync(execution.StderrPath, stderrOffset, result.Status == "running", cancellationToken);
        return result with
        {
            ProcessId = execution.Id, Stdout = stdout.Text, Stderr = stderr.Text,
            StdoutPath = execution.StdoutPath, StderrPath = execution.StderrPath,
            StdoutOffset = stdout.Offset, StderrOffset = stderr.Offset,
            HasMoreOutput = stdout.More || stderr.More,
            Truncated = stdout.More || stderr.More
        };
    }

    private static async Task<(string Text, long Offset, bool More)> ReadLogAsync(string path, long offset, bool running, CancellationToken token)
    {
        if (!File.Exists(path)) return ("", offset, false);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
        if (offset > stream.Length) throw new ArgumentException("Output offset exceeds the log length.");
        stream.Position = offset;
        // At most one response-sized page. The full original byte stream stays in the file.
        var count = (int)Math.Min(ProcessRunner.OutputLimit, stream.Length - offset);
        var bytes = new byte[count];
        await stream.ReadExactlyAsync(bytes, token);
        // Do not consume an incomplete UTF-8 character at a page boundary or while the writer is active.
        var keep = count;
        if (running || stream.Position < stream.Length)
        {
            var start = count - 1;
            while (start >= Math.Max(0, count - 3) && (bytes[start] & 0xC0) == 0x80) start--;
            if (start >= 0)
            {
                var lead = bytes[start];
                var width = lead >= 0xF0 ? 4 : lead >= 0xE0 ? 3 : lead >= 0xC2 ? 2 : 1;
                if (start + width > count) keep = start;
            }
        }
        using var memory = new MemoryStream(bytes, 0, keep, writable: false, publiclyVisible: true);
        var (text, _) = ProcessRunner.Decode(memory, false);
        var next = offset + keep;
        return (text, next, next < stream.Length);
    }

    private static void ValidateWait(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxWaitTimeoutMs);
    }
    private static ProcessResult Failure(string error) => new(null, "", "", 0, false, false, error, Status: "failed");

    public async ValueTask DisposeAsync()
    {
        Execution[] executions;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            executions = _executions.Values.ToArray();
            foreach (var execution in executions) execution.Stop.Cancel();
        }
        await Task.WhenAll(executions.Select(item => item.Result));
        foreach (var execution in executions) execution.Stop.Dispose();
    }

    private sealed class Execution(string id, string directory)
    {
        public string Id { get; } = id;
        public DateTimeOffset Started { get; } = DateTimeOffset.UtcNow;
        public Stopwatch Timer { get; } = Stopwatch.StartNew();
        public string StdoutPath { get; } = Path.Combine(directory, "stdout.log");
        public string StderrPath { get; } = Path.Combine(directory, "stderr.log");
        public CancellationTokenSource Stop { get; } = new();
        public Task<ProcessResult> Result { get; set; } = null!;
    }
}
