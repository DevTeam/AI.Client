namespace AI.Client.Infrastructure.Storage;

using AI.Client.Domain.Projects;

public sealed class ProjectStoragePaths(string rootDirectory)
{
    public string ProjectsDirectory => Path.Combine(rootDirectory, "projects");

    public string GetProjectPath(ProjectId id) =>
        Path.Combine(ProjectsDirectory, $"{id}.json");

    public string GetTemporaryProjectPath(ProjectId id) =>
        Path.Combine(ProjectsDirectory, $"{id}.json.tmp");
}
