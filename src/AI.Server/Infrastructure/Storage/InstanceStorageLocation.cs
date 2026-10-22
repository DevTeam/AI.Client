namespace AI.Infrastructure.Storage;

using AI.Server.Hosting;

/// <summary>
/// The storage root the entry point chose (see <see cref="ServerOptions.DataDirectory"/>). The
/// value lives behind <see cref="IInstanceStorageLocation"/> so the file logger and every user root
/// are derived from one place.
/// </summary>
public sealed class InstanceStorageLocation(ServerOptions options) : IInstanceStorageLocation
{
    public string RootDirectory { get; } = Path.GetFullPath(options.DataDirectory);
}
