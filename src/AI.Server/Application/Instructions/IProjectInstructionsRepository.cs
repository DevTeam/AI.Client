namespace AI.Application.Instructions;

using AI.Contracts.Instructions;

/// <summary>One instructions document per project, with an optimistic revision check on save.</summary>
public interface IProjectInstructionsRepository
{
    /// <summary>The stored document, or an empty one at revision 0 when the project has none yet.</summary>
    Task<ProjectInstructions> GetAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>Saves unless the stored revision differs; returns the stored document either way.</summary>
    Task<(bool Saved, ProjectInstructions Current)> SaveAsync(Guid projectId, string text, bool includeWorkspaceFiles,
        long expectedRevision, DateTimeOffset updatedAt, CancellationToken cancellationToken);

    Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken);
}
