namespace AI.Infrastructure.Instances;

/// <summary>
/// Keeps one desktop process per data directory. A second launch on the same directory does not
/// start another one over it: it hands its request to the running process and exits.
/// </summary>
public interface ISingleInstance
{
    /// <summary>
    /// Becomes the instance of <paramref name="dataDirectory"/>, or hands <paramref name="request"/>
    /// to the instance that already is. The returned claim holds the role until it is disposed.
    /// </summary>
    IInstanceClaim Claim(string dataDirectory, InstanceRequest request);
}

/// <summary>What a later launch asks the running instance for.</summary>
public enum InstanceRequest : byte
{
    /// <summary>Bring a window to the front.</summary>
    Activate = 1,

    /// <summary>Open one more window.</summary>
    NewWindow = 2
}

/// <summary>What one launch became for its data directory.</summary>
public interface IInstanceClaim : IDisposable
{
    /// <summary>The running instance took this launch's request: this process has nothing left to show.</summary>
    bool IsForwarded { get; }

    /// <summary>
    /// Called, on a background thread, with each request of a later launch. Requests that came
    /// before the handler was set are delivered to it at once. The handler returns false once
    /// this process is closing and has no window to offer: the launch then waits for it to exit
    /// and takes over.
    /// </summary>
    void OnRequest(Func<InstanceRequest, bool> handle);
}
