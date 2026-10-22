namespace AI.Application.Instructions;

using AI.Contracts.Instructions;
using Projects;

public sealed class ProjectInstructionsService(IProjectInstructionsRepository repository, IProjectService projects, IClock clock)
    : IProjectInstructionsService
{
    /// <summary>
    /// Generous for hand-written rules, yet small enough that the layer's token budget, not this
    /// limit, is what a long document normally runs into.
    /// </summary>
    public const int TextLimit = 32_000;

    public async Task<ProjectInstructions?> GetAsync(Guid projectId, CancellationToken cancellationToken) =>
        await projects.GetAsync(projectId, cancellationToken) is null
            ? null
            : await repository.GetAsync(projectId, cancellationToken);

    public async Task<ProjectInstructionsUpdateResult> UpdateAsync(Guid projectId, UpdateProjectInstructionsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await projects.GetAsync(projectId, cancellationToken) is null)
            return new ProjectInstructionsUpdateResult(ProjectInstructionsUpdateStatus.NotFound, null, "Project not found.");
        var text = (request.Text ?? string.Empty).Trim();
        if (text.Length > TextLimit)
            return new ProjectInstructionsUpdateResult(ProjectInstructionsUpdateStatus.Rejected, null,
                $"Project instructions are longer than {TextLimit} characters.");
        var (saved, current) = await repository.SaveAsync(projectId, text, request.IncludeWorkspaceFiles,
            request.Revision, clock.UtcNow, cancellationToken);
        return saved
            ? new ProjectInstructionsUpdateResult(ProjectInstructionsUpdateStatus.Updated, current)
            : new ProjectInstructionsUpdateResult(ProjectInstructionsUpdateStatus.Conflict, current,
                "The revision does not match the stored one.");
    }

    public Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        repository.DeleteProjectAsync(projectId, cancellationToken);
}
