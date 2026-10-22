namespace AI.Infrastructure.Storage;

/// <summary>
/// The data root, for what belongs to the installation rather than to a user: the master key, logs,
/// browser grants and the directory lock. User data lives under <see cref="IUserStorageLocation"/>.
/// </summary>
public interface IInstanceStorageLocation
{
    /// <summary>Absolute path of the root directory the application owns.</summary>
    string RootDirectory { get; }
}
