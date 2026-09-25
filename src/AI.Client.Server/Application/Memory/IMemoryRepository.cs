namespace AI.Client.Application.Memory;

using AI.Client.Contracts.Memory;

public enum MemoryWriteStatus { Saved, NotFound, Conflict, Rejected }

/// <summary>
/// The outcome of one memory mutation. A conflict carries the stored entry so the caller can show
/// or re-read what someone else wrote first.
/// </summary>
public sealed record MemoryWriteResult(MemoryWriteStatus Status, MemoryEntry? Entry, string? Error = null)
{
    public static MemoryWriteResult Saved(MemoryEntry entry) => new(MemoryWriteStatus.Saved, entry);
    public static MemoryWriteResult NotFound() => new(MemoryWriteStatus.NotFound, null, "Memory entry not found.");
    public static MemoryWriteResult Conflict(MemoryEntry current) =>
        new(MemoryWriteStatus.Conflict, current, "The revision does not match the stored one.");
    public static MemoryWriteResult Rejected(string error) => new(MemoryWriteStatus.Rejected, null, error);
}

/// <summary>
/// One catalog per owner: the person's own (<c>projectId</c> null) and one per project. Revisions
/// are checked inside the repository's write boundary.
/// </summary>
public interface IMemoryRepository
{
    Task<IReadOnlyList<MemoryEntry>> ListAsync(Guid? projectId, CancellationToken cancellationToken);

    /// <summary>Adds the entry unless the catalog already holds <paramref name="capacity"/> entries.</summary>
    Task<MemoryWriteResult> AddAsync(Guid? projectId, MemoryEntry entry, int capacity, CancellationToken cancellationToken);

    Task<MemoryWriteResult> UpdateAsync(Guid? projectId, Guid id, long expectedRevision,
        Func<MemoryEntry, MemoryEntry> change, CancellationToken cancellationToken);

    Task<MemoryWriteResult> DeleteAsync(Guid? projectId, Guid id, long expectedRevision, CancellationToken cancellationToken);

    Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken);
}
