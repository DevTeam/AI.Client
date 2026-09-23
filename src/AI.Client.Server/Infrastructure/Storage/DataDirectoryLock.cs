namespace AI.Client.Infrastructure.Storage;

/// <summary>
/// Holds <c>&lt;data&gt;/.lock</c> open without sharing (a sharing lock on Windows, <c>flock</c> on
/// macOS and Linux). The operating system releases it when the process ends however it ends, so a
/// crash never leaves the directory locked. The file itself stays: deleting it on close would let
/// a second process lock a fresh file while a third still holds the unlinked one.
/// </summary>
public sealed class DataDirectoryLock(IProjectStorageLocation location) : IDataDirectoryLock
{
    public IDisposable Acquire()
    {
        Directory.CreateDirectory(location.RootDirectory);
        var path = Path.Combine(location.RootDirectory, ".lock");
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException error)
        {
            throw new DataDirectoryInUseException(
                $"The data directory '{location.RootDirectory}' is already used by another AI.Client process. " +
                "Close it, or start this one with a different --data-dir.", error);
        }
    }
}
