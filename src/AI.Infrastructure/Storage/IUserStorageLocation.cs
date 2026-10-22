namespace AI.Infrastructure.Storage;

/// <summary>The directory that holds the current user's data (ADR-014).</summary>
public interface IUserStorageLocation
{
    /// <summary>
    /// Absolute path of the current user's root, read again on every call: a singleton that kept it
    /// would keep one user's directory for all of them.
    /// </summary>
    string RootDirectory { get; }
}
