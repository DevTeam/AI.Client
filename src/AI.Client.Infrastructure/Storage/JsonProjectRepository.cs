using AI.Client.Application.Projects;
using AI.Client.Contracts.Projects;
using AI.Client.Domain.Projects;

namespace AI.Client.Infrastructure.Storage;

public sealed class JsonProjectRepository(
    ITextFileSystem fileSystem,
    IProjectStoragePaths paths,
    IProjectDocumentSerializer serializer) : IProjectRepository
{
    public async Task<Project?> GetAsync(ProjectId id, CancellationToken cancellationToken)
    {
        await RecoverAsync(id, cancellationToken);
        var json = await fileSystem.ReadTextAsync(paths.GetProjectPath(id), cancellationToken);
        return json is null ? null : serializer.Deserialize(json).Project;
    }

    public async Task<IReadOnlyList<StoredProject>> ListAsync(CancellationToken cancellationToken)
    {
        var files = await fileSystem.ListFilesAsync(paths.ProjectsDirectory, "*.json", cancellationToken);
        var projects = new List<StoredProject>();
        foreach (var path in files)
        {
            var json = await fileSystem.ReadTextAsync(path, cancellationToken);
            if (json is not null)
            {
                projects.Add(serializer.Deserialize(json));
            }
        }

        return projects;
    }

    public async Task<ProjectSaveResult> SaveAsync(
        Project project,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);

        await RecoverAsync(project.Id, cancellationToken);
        var projectPath = paths.GetProjectPath(project.Id);
        var currentJson = await fileSystem.ReadTextAsync(projectPath, cancellationToken);
        var currentRevision = currentJson is null ? 0 : serializer.Deserialize(currentJson).Revision;
        if (currentRevision != expectedRevision)
        {
            return ProjectSaveResult.Conflict(currentRevision);
        }

        var nextRevision = checked(currentRevision + 1);
        var temporaryPath = paths.GetTemporaryProjectPath(project.Id);
        await fileSystem.WriteTextAsync(temporaryPath, serializer.Serialize(project, nextRevision), cancellationToken);
        await fileSystem.MoveAsync(temporaryPath, projectPath, true, cancellationToken);
        return ProjectSaveResult.Saved(nextRevision);
    }

    public async Task<ProjectDeleteResult> DeleteAsync(
        ProjectId id,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        await RecoverAsync(id, cancellationToken);
        var projectPath = paths.GetProjectPath(id);
        var currentJson = await fileSystem.ReadTextAsync(projectPath, cancellationToken);
        if (currentJson is null)
        {
            return ProjectDeleteResult.NotFound();
        }

        var currentRevision = serializer.Deserialize(currentJson).Revision;
        if (currentRevision != expectedRevision)
        {
            return ProjectDeleteResult.Conflict(currentRevision);
        }

        await fileSystem.DeleteAsync(projectPath, cancellationToken);
        return ProjectDeleteResult.Deleted(currentRevision);
    }

    private async Task RecoverAsync(ProjectId id, CancellationToken cancellationToken)
    {
        var temporaryPath = paths.GetTemporaryProjectPath(id);
        if (!await fileSystem.ExistsAsync(temporaryPath, cancellationToken))
        {
            return;
        }

        var projectPath = paths.GetProjectPath(id);
        if (await fileSystem.ExistsAsync(projectPath, cancellationToken))
        {
            await fileSystem.DeleteAsync(temporaryPath, cancellationToken);
            return;
        }

        await fileSystem.MoveAsync(temporaryPath, projectPath, false, cancellationToken);
    }
}
