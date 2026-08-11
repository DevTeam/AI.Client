using AI.Client.Domain.Projects;

namespace AI.Client.Infrastructure.Storage;

public interface IProjectStoragePaths
{
    string ProjectsDirectory { get; }

    string GetProjectPath(ProjectId id);

    string GetTemporaryProjectPath(ProjectId id);
}
