namespace AI.Client.Application.Instructions;

using AI.Client.Contracts.Instructions;

public enum ProjectInstructionsUpdateStatus { Updated, NotFound, Conflict, Rejected }

public sealed record ProjectInstructionsUpdateResult(
    ProjectInstructionsUpdateStatus Status,
    ProjectInstructions? Instructions,
    string? Error = null);

public interface IProjectInstructionsService
{
    Task<ProjectInstructions?> GetAsync(Guid projectId, CancellationToken cancellationToken);

    Task<ProjectInstructionsUpdateResult> UpdateAsync(Guid projectId, UpdateProjectInstructionsRequest request,
        CancellationToken cancellationToken);

    Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken);
}
