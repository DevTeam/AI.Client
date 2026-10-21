namespace AI.Sandbox;

using System.Diagnostics;

/// <summary>
/// Starts the processes a tool call runs, with everything that bounds them: the directories a
/// <see cref="SandboxPolicy"/> opens, the <see cref="ChildProcessLimits"/> of the whole process tree,
/// and a deadline after which the tree is killed. The program sees ordinary redirected streams and
/// the caller an ordinary <see cref="Process"/>, so a bounded program is handled exactly like any
/// other.
/// </summary>
public interface IProcessLauncher
{
    /// <summary>Whether and how this machine can enforce a <see cref="SandboxPolicy"/>.</summary>
    SandboxSupport Support { get; }

    /// <summary>
    /// Starts the program <paramref name="launch"/> describes inside its bounds. A policy this
    /// machine cannot enforce, or limits it cannot apply, fail the start rather than letting the
    /// program run unbounded.
    /// </summary>
    /// <exception cref="SandboxUnavailableException">A policy was given and <see cref="Support"/> says it cannot be enforced.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">The program does not exist or cannot be started.</exception>
    ILaunchedProcess Start(ProcessLaunch launch);
}

/// <param name="Start">The program, its arguments, working directory, environment and redirections; never through the shell.</param>
/// <param name="Policy">The file system the tree may reach, or <c>null</c> to leave it unconfined.</param>
/// <param name="Limits">The resources the tree may use; <c>null</c> means <see cref="ChildProcessLimits.Default"/>.</param>
/// <param name="TimeoutMs">Milliseconds from the start after which the tree is killed; <c>null</c> means no deadline.</param>
public sealed record ProcessLaunch(
    ProcessStartInfo Start,
    SandboxPolicy? Policy = null,
    ChildProcessLimits? Limits = null,
    int? TimeoutMs = null);

/// <summary>
/// A started program and the bounds it runs in. Disposing it kills whatever is left of the tree,
/// so a caller that gives up on a run cannot leave it behind.
/// </summary>
public interface ILaunchedProcess : IDisposable
{
    /// <summary>
    /// The started process, with the streams the start info redirected. Under a sandbox on Windows
    /// this is the launcher that holds the confined program; it forwards the streams and the exit
    /// code, so the difference does not show.
    /// </summary>
    Process Process { get; }

    /// <summary>Cancelled when the deadline passes, so reads of the streams can stop with the tree.</summary>
    CancellationToken Deadline { get; }

    /// <summary>Whether the tree was killed because the deadline passed.</summary>
    bool TimedOut { get; }

    /// <summary>
    /// <see cref="ChildProcessJob.Memory"/> or <see cref="ChildProcessJob.Processes"/> when the tree
    /// ran into that limit, otherwise <c>null</c>. Read it after the tree has exited.
    /// </summary>
    string? LimitExceeded { get; }

    /// <summary>
    /// Waits until the process exits or the deadline passes, killing the tree in the second case.
    /// Cancellation kills the tree too, then throws.
    /// </summary>
    Task WaitForExitAsync(CancellationToken cancellationToken);

    /// <summary>Kills the whole tree; a tree that already exited is left as it is.</summary>
    void Kill();
}

/// <param name="Available">Whether a policy is enforced on this machine.</param>
/// <param name="Mechanism">The OS facility that enforces it: AppContainer, Landlock or Seatbelt.</param>
/// <param name="Reason">Why it is not available, when it is not.</param>
public sealed record SandboxSupport(bool Available, string Mechanism, string? Reason = null);

public sealed class SandboxUnavailableException : InvalidOperationException
{
    public SandboxUnavailableException()
    {
    }

    public SandboxUnavailableException(string message) : base(message)
    {
    }

    public SandboxUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
