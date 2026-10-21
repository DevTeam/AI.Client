namespace AI.Sandbox;

using System.ComponentModel;
using System.Diagnostics;

/// <summary>A started process, the job that bounds its tree and the deadline that ends it.</summary>
internal sealed class LaunchedProcess : ILaunchedProcess
{
    private readonly ChildProcessJob _job;
    private readonly CancellationTokenSource _deadline = new();

    public LaunchedProcess(Process process, ChildProcessJob job, int? timeoutMs)
    {
        Process = process;
        _job = job;
        if (timeoutMs is { } timeout)
        {
            _deadline.CancelAfter(timeout);
        }
    }

    public Process Process { get; }

    public CancellationToken Deadline => _deadline.Token;

    public bool TimedOut { get; private set; }

    public string? LimitExceeded => _job.LimitExceeded;

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _deadline.Token);
        try
        {
            await Process.WaitForExitAsync(wait.Token);
        }
        catch (OperationCanceledException) when (wait.IsCancellationRequested)
        {
            TimedOut = !cancellationToken.IsCancellationRequested;
            Kill();
            await Process.WaitForExitAsync(CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    public void Kill()
    {
        try
        {
            Process.Kill(entireProcessTree: true);
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
            if (!Process.HasExited)
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
        Process.Dispose();
        _deadline.Dispose();
    }
}
