namespace AI.Client.Application.Memory;

using AI.Client.Contracts.Memory;

/// <summary>
/// Long-term memory about the person and their projects. The UI and the model's tools share it,
/// so validation and limits live here rather than in either caller.
/// </summary>
public interface IMemoryService
{
    /// <summary>The person's entries, followed by the project's when <paramref name="projectId"/> is given.</summary>
    Task<IReadOnlyList<MemoryEntry>> ListAsync(Guid? projectId, CancellationToken cancellationToken);

    /// <summary>Finds an entry in the person's catalog or, when given, in the project's.</summary>
    Task<MemoryEntry?> GetAsync(Guid id, Guid? projectId, CancellationToken cancellationToken);

    /// <summary>Enabled entries whose title, body or tags contain every word of the query, best match first.</summary>
    Task<IReadOnlyList<MemoryEntry>> SearchAsync(string query, Guid? projectId, CancellationToken cancellationToken);

    Task<MemoryWriteResult> CreateAsync(CreateMemoryEntryRequest request, MemoryAuthor author, Guid? chatId,
        CancellationToken cancellationToken);

    Task<MemoryWriteResult> UpdateAsync(Guid id, Guid? projectId, UpdateMemoryEntryRequest request, MemoryAuthor author,
        Guid? chatId, CancellationToken cancellationToken);

    Task<MemoryWriteResult> DeleteAsync(Guid id, Guid? projectId, long expectedRevision, CancellationToken cancellationToken);

    Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken);
}
