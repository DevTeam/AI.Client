namespace AI.Client.Infrastructure.Storage;

/// <summary>
/// Resolves the storage root from the <c>AI_CLIENT_DATA_DIRECTORY</c> environment variable, falling
/// back to the platform's local application data directory. The value lives behind
/// <see cref="IProjectStorageLocation"/> so the container and the file logger both read it from one
/// place.
/// </summary>
public sealed class ProjectStorageLocation : IProjectStorageLocation
{
    public ProjectStorageLocation()
    {
        RootDirectory = Environment.GetEnvironmentVariable("AI_CLIENT_DATA_DIRECTORY") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AI.Client");
    }

    public string RootDirectory { get; }
}
