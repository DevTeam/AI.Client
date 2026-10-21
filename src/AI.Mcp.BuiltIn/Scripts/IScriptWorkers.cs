namespace AI.Mcp.BuiltIn.Scripts;

using AI.Sandbox;

/// <summary>The worker processes scripts run in, one script per worker.</summary>
public interface IScriptWorkers
{
    /// <summary>
    /// A started worker, waiting for its request on standard input, bounded by
    /// <paramref name="limits"/> and the session's sandbox. The caller owns and disposes it.
    /// </summary>
    ILaunchedProcess Take(ChildProcessLimits limits);

    /// <summary>Starts the next worker ahead of time if none is waiting.</summary>
    void Replenish();
}
