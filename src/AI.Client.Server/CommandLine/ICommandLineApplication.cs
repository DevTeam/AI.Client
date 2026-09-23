namespace AI.Client.Server.CommandLine;

/// <summary>An executable's entry point: parses the arguments and runs what they ask for.</summary>
public interface ICommandLineApplication
{
    Task<int> RunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs the chosen action on the calling thread. A UI entry point needs this: the desktop app
    /// must start its UI on the main thread (macOS requires it, WebView2 needs an STA thread),
    /// which an asynchronous invocation would leave for a thread-pool one.
    /// </summary>
    int Run();
}
