namespace AI.Infrastructure.Storage;

using AI.Domain.Projects;

public interface IProjectStoragePaths
{
    string ProjectsDirectory { get; }
    string GetProjectPath(ProjectId id);
    string GetTemporaryProjectPath(ProjectId id);
}
