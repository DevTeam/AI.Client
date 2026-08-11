namespace AI.Client.Infrastructure.Storage;

public sealed class ProjectStorageLocation
{
    public ProjectStorageLocation()
    {
        RootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AI.Client");
    }

    public string RootDirectory { get; }
}
