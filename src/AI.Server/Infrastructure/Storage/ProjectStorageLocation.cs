namespace AI.Infrastructure.Storage;

using AI.Server.Hosting;

/// <summary>
/// The storage root the entry point chose (see <see cref="ServerOptions.DataDirectory"/>). The
/// value lives behind <see cref="IProjectStorageLocation"/> so the repositories and the file
/// logger all read it from one place.
/// </summary>
public sealed class ProjectStorageLocation(ServerOptions options) : IProjectStorageLocation
{
    public string RootDirectory { get; } = Path.GetFullPath(options.DataDirectory);
}
