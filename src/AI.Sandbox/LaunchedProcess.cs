namespace AI.Sandbox;

using System.ComponentModel;
using System.Diagnostics;

/// <summary>A started process, its streams, the job that bounds its tree and the deadline that ends it.</summary>
internal sealed class LaunchedProcess : ILaunchedProcess
{
    private readonly Process _process;
    private readonly ChildProcessJob _job;
    private readonly IDisposable? _handle;
    private readonly CancellationTokenSource _deadline = new();

    /// <param name="handle">A handle that keeps the process addressable until it is disposed, when the start opened one of its own.</param>
    public LaunchedProcess(Process process, StreamWriter input, StreamReader output, StreamReader error,
        ChildProcessJob job, IDisposable? handle, int? timeoutMs)
    {
        _process = process;
        _job = job;
        _handle = handle;
        StandardInput = input;
        StandardOutput = output;
        StandardError = error;
        if (timeoutMs is { } timeout)
        {
            _deadline.CancelAfter(timeout);
        }
    }

    public StreamWriter StandardInput { get; }

    public StreamReader StandardOutput { get; }

    public StreamReader StandardError { get; }

    public int Id => _process.Id;

    public bool HasExited => _process.HasExited;

    public int ExitCode => _process.ExitCode;

    public CancellationToken Deadline => _deadline.Token;

    public bool TimedOut { get; private set; }

    public string? LimitExceeded => _job.LimitExceeded;

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _deadline.Token);
        try
        {
            await _process.WaitForExitAsync(wait.Token);
        }
        catch (OperationCanceledException) when (wait.IsCancellationRequested)
        {
            TimedOut = !cancellationToken.IsCancellationRequested;
            Kill();
            await _process.WaitForExitAsync(CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    public void Kill()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception)
        {
            // The process already exited, or the OS refused; the job still ends the tree on Windows.
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // The process was never started or its handle is gone.
        }

        // Closing the job kills anything still in it on Windows, descendants whose parent already
        // exited included.
        _job.Dispose();
        Quietly(StandardInput);
        Quietly(StandardOutput);
        Quietly(StandardError);
        _process.Dispose();
        _handle?.Dispose();
        _deadline.Dispose();
    }

    /// <summary>Closes a stream whose other end may be gone: flushing into a closed pipe is no failure here.</summary>
    private static void Quietly(IDisposable stream)
    {
        try
        {
            stream.Dispose();
        }
        catch (IOException)
        {
            // The program exited without reading what was left in the buffer.
        }
    }
}
