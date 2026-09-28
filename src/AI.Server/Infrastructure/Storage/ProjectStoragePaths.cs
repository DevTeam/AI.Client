namespace AI.Infrastructure.Storage;

using AI.Domain.Projects;

public sealed class ProjectStoragePaths(IProjectStorageLocation location) : IProjectStoragePaths
{
    public string ProjectsDirectory => Path.Combine(location.RootDirectory, "projects");

    public string GetProjectPath(ProjectId id) =>
        Path.Combine(ProjectsDirectory, $"{id}.json");

    public string GetTemporaryProjectPath(ProjectId id) =>
        Path.Combine(ProjectsDirectory, $"{id}.json.tmp");
}
