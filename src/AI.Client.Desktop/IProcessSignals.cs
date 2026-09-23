namespace AI.Client.Desktop;

/// <summary>The requests to end the process that arrive as signals: Ctrl+C, SIGTERM, SIGQUIT.</summary>
internal interface IProcessSignals
{
    /// <summary>Calls <paramref name="terminate"/> instead of letting a signal end the process.</summary>
    /// <returns>A handle that stops listening when disposed.</returns>
    IDisposable OnTermination(Action terminate);
}
