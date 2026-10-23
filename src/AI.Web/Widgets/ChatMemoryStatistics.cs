namespace AI.Web.Widgets;

using AI.Contracts.Memory;

/// <summary>One enabled memory entry the open chat works with.</summary>
/// <param name="ChatId">The chat the assistant wrote the entry in; null for entries the person wrote.</param>
public sealed record ChatMemoryEntry(
    Guid Id,
    string Title,
    MemoryKind Kind,
    MemoryScope Scope,
    bool Pinned,
    MemoryAuthor Author,
    Guid? ChatId,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Whether the assistant saved this entry from the chat the widget is shown in.</summary>
    public bool IsFromChat(Guid chatId) => ChatId == chatId;
}

/// <summary>
/// The memory the open chat works with: the person's own entries, which every chat sees, and the
/// entries of the chat's project. Only enabled entries count, because only those reach a run.
/// </summary>
/// <param name="Entries">The entries in the order the widget lists them: pinned first, then the ones saved in this chat, then newest first.</param>
/// <param name="FromThisChat">Entries the assistant saved from this chat, whichever catalog holds them.</param>
/// <param name="HasProject">False when the chat has no project, so only the person's own entries apply.</param>
public sealed record ChatMemoryStatistics(
    IReadOnlyList<ChatMemoryEntry> Entries,
    int FromThisChat,
    bool HasProject)
{
    public static ChatMemoryStatistics Empty { get; } = new([], 0, false);

    public int Total => Entries.Count;

    public int Pinned => Entries.Count(entry => entry.Pinned);

    public int ProjectEntries => Entries.Count(entry => entry.Scope == MemoryScope.Project);

    public int UserEntries => Entries.Count(entry => entry.Scope == MemoryScope.User);

    public int Count(MemoryKind kind) => Entries.Count(entry => entry.Kind == kind);
}

/// <summary>
/// Selects and orders the memory entries the open chat depends on. The input is the catalog the
/// memory editor shows — the person's entries followed by the project's — so the widget and the
/// settings drawers agree on what is stored.
/// </summary>
public interface IChatMemoryStatisticsCalculator
{
    /// <param name="entries">Every stored entry of the person and of the project, enabled or not.</param>
    /// <param name="projectId">The project of the open chat; null for a chat that has none yet.</param>
    /// <param name="chatId">The open chat, so entries the assistant saved here are marked; null for a chat without one.</param>
    ChatMemoryStatistics Calculate(IReadOnlyList<MemoryEntry> entries, Guid? projectId, Guid? chatId);
}

public sealed class ChatMemoryStatisticsCalculator : IChatMemoryStatisticsCalculator
{
    public ChatMemoryStatistics Calculate(IReadOnlyList<MemoryEntry> entries, Guid? projectId, Guid? chatId)
    {
        // A disabled entry is stored but reaches no run, so counting it would describe a chat that
        // is not the one on screen. An entry of another project never applies here either.
        var applicable = entries
            .Where(entry => entry.Enabled)
            .Where(entry => entry.Scope == MemoryScope.User
                            || projectId is { } project && project != Guid.Empty && entry.ProjectId == project)
            .DistinctBy(entry => entry.Id)
            .Select(entry => new ChatMemoryEntry(entry.Id, entry.Title, entry.Kind, entry.Scope, entry.Pinned,
                entry.Author, entry.ChatId, entry.UpdatedAt))
            .OrderByDescending(entry => entry.Pinned)
            .ThenByDescending(entry => chatId is { } chat && entry.IsFromChat(chat))
            .ThenByDescending(entry => entry.UpdatedAt)
            .ThenBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var fromThisChat = chatId is { } current
            ? applicable.Count(entry => entry.IsFromChat(current))
            : 0;
        return new ChatMemoryStatistics(applicable, fromThisChat,
            projectId is { } id && id != Guid.Empty);
    }
}
