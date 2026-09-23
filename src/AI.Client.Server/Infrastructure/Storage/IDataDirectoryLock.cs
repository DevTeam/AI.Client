namespace AI.Client.Infrastructure.Storage;

/// <summary>
/// Makes one server the only writer of a data directory. The repositories keep documents in
/// memory and write them back whole, so two processes over the same files would silently undo
/// each other's changes — the standalone host and the desktop app included.
/// </summary>
public interface IDataDirectoryLock
{
    /// <summary>Takes the directory for as long as the returned handle is not disposed.</summary>
    /// <exception cref="DataDirectoryInUseException">Another process holds it.</exception>
    IDisposable Acquire();
}
