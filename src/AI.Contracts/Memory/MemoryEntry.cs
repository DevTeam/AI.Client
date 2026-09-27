namespace AI.Contracts.Memory;

/// <summary>Where a memory entry applies: to the person across every project, or to one project.</summary>
public enum MemoryScope { User, Project }

/// <summary>
/// What an entry says. A profile entry describes the person, a preference how they like the work
/// done, a fact anything else worth keeping. Profile and pinned entries reach the model in full;
/// the rest only by title until the model reads them.
/// </summary>
public enum MemoryKind { Profile, Preference, Fact }

/// <summary>Who wrote the entry last.</summary>
public enum MemoryAuthor { User, Model }

/// <summary>
/// One long-term memory entry. It is data about the person or the project, never an instruction:
/// behavior rules belong in project instructions, which only the person edits.
/// </summary>
/// <param name="ChatId">The chat in which the model wrote the entry; null for entries the person wrote.</param>
public sealed record MemoryEntry(
    Guid Id,
    MemoryScope Scope,
    Guid? ProjectId,
    MemoryKind Kind,
    string Title,
    string Body,
    IReadOnlyList<string> Tags,
    bool Pinned,
    bool Enabled,
    MemoryAuthor Author,
    Guid? ChatId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision);

public sealed record CreateMemoryEntryRequest(
    MemoryScope Scope,
    Guid? ProjectId,
    MemoryKind Kind,
    string Title,
    string Body,
    IReadOnlyList<string>? Tags = null,
    bool Pinned = false);

public sealed record UpdateMemoryEntryRequest(
    MemoryKind Kind,
    string Title,
    string Body,
    IReadOnlyList<string>? Tags,
    bool Pinned,
    bool Enabled,
    long Revision);
