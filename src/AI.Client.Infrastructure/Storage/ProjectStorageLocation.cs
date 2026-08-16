namespace AI.Client.Infrastructure.Storage;

public sealed class ProjectStorageLocation
{
    public ProjectStorageLocation()
    {
        RootDirectory = Environment.GetEnvironmentVariable("AI_CLIENT_DATA_DIRECTORY") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AI.Client");
    }

    public string RootDirectory { get; }
}
