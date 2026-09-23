namespace AI.Client.Infrastructure.Storage;

using AI.Client.Domain.Projects;

public interface IProjectStoragePaths
{
    string ProjectsDirectory { get; }
    string GetProjectPath(ProjectId id);
    string GetTemporaryProjectPath(ProjectId id);
}
