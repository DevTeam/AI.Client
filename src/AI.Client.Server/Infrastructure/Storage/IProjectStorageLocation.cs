namespace AI.Client.Infrastructure.Storage;

/// <summary>
/// Decides where the application writes its data and logs. A single value reaches both the
/// container and the file logger, so the two never disagree about the storage root.
/// </summary>
public interface IProjectStorageLocation
{
    /// <summary>Absolute path of the root directory the application owns.</summary>
    string RootDirectory { get; }
}
