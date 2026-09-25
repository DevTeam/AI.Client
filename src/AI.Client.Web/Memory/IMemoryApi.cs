namespace AI.Client.Web.Memory;

using AI.Client.Contracts.Instructions;
using AI.Client.Contracts.Memory;

/// <summary>
/// Long-term memory and project instructions. A failed write throws
/// <see cref="InvalidOperationException"/> with a message fit for the person; a conflict throws
/// <see cref="MemoryConflictException"/> so the caller can reload what is stored.
/// </summary>
public interface IMemoryApi
{
    Task<IReadOnlyList<MemoryEntry>> ListAsync(Guid? projectId, CancellationToken cancellationToken);
    Task<MemoryEntry> CreateAsync(CreateMemoryEntryRequest request, CancellationToken cancellationToken);
    Task<MemoryEntry> UpdateAsync(MemoryEntry entry, UpdateMemoryEntryRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(MemoryEntry entry, CancellationToken cancellationToken);
    Task<ProjectInstructions> GetInstructionsAsync(Guid projectId, CancellationToken cancellationToken);
    Task<ProjectInstructions> UpdateInstructionsAsync(Guid projectId, UpdateProjectInstructionsRequest request,
        CancellationToken cancellationToken);
    Task<ModelContextPreview> GetModelContextAsync(Guid projectId, CancellationToken cancellationToken);
}

public sealed class MemoryConflictException() : InvalidOperationException("Someone changed this in the meantime. The stored version is shown again.");
