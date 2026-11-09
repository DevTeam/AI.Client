namespace AI.Infrastructure.Instances;

/// <summary>
/// Keeps one desktop window per data directory. A second launch on the same directory does not
/// open another window over it: it asks the running one to come to the front and exits.
/// </summary>
public interface ISingleInstance
{
    /// <summary>
    /// Becomes the instance of <paramref name="dataDirectory"/>, or hands this launch to the
    /// instance that already is. The returned claim holds the role until it is disposed.
    /// </summary>
    IInstanceClaim Claim(string dataDirectory);
}

/// <summary>What one launch became for its data directory.</summary>
public interface IInstanceClaim : IDisposable
{
    /// <summary>The running instance took this launch: this process has nothing left to show.</summary>
    bool IsForwarded { get; }

    /// <summary>
    /// Called, on a background thread, each time a later launch asks this instance to come to the
    /// front. A request that came before the handler was set is delivered to it at once. The
    /// handler returns false once there is no window to show any more: the launch then waits for
    /// this process to exit and takes over.
    /// </summary>
    void OnActivation(Func<bool> activate);
}
