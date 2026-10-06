namespace AI.Web.State;

/// <summary>
/// Client-only "where was I" memory (last project, last chat/branch per project, unsent composer
/// drafts, sent-message history), persisted to localStorage. Unrelated to chat data itself,
/// which the server already owns — this is purely a UI convenience so the app reopens where it
/// was left.
/// </summary>
public interface IWorkspaceStateService : IAsyncDisposable
{
    /// <summary>Loads everything from localStorage. Safe to call more than once; only the first call does any work.</summary>
    Task InitializeAsync();

    /// <summary>The project id last selected, or null if none was ever recorded.</summary>
    Guid? LastProjectId { get; }

    /// <summary>Records the given project as the last one selected. No-op (no write) if it's already the current value.</summary>
    Task SetLastProjectIdAsync(Guid projectId);

    /// <summary>
    /// The directory the picker was last looking at, so it reopens where it was left instead of at
    /// the drive list. Machine-wide rather than per project: it is where the user keeps their code,
    /// and that does not change when they switch project.
    /// </summary>
    string? LastBrowsedDirectory { get; }

    /// <summary>Records where the picker ended up. No-op for an empty or unchanged path.</summary>
    Task SetLastBrowsedDirectoryAsync(string path);

    /// <summary>The chat/branch last selected within the given project, if any.</summary>
    (Guid? ChatId, Guid? BranchLeafId, Guid? BranchId) GetProjectContext(Guid projectId);

    /// <summary>Records the chat/branch last selected within the given project. No-op (no write) if unchanged.</summary>
    Task SetProjectContextAsync(Guid projectId, Guid? chatId, Guid? branchLeafId, Guid? branchId = null);

    /// <summary>
    /// Messages already sent from this project's composer, newest first. Shared by every chat in
    /// the project — the reuse this exists for happens across chats, not inside one.
    /// </summary>
    IReadOnlyList<string> GetComposerHistory(Guid projectId);

    /// <summary>
    /// Records a sent message at the front of the project's history and persists immediately.
    /// Blank text is ignored; an identical earlier entry is moved to the front instead of
    /// being duplicated; the list is capped at 100 entries.
    /// </summary>
    Task AppendComposerHistoryAsync(Guid projectId, string text);

    /// <summary>The unsent composer draft for the given key (chat id, or "new:{projectId}" before the first message creates the chat).</summary>
    string GetComposerDraft(string draftKey);

    /// <summary>Unsent resource links saved beside the text draft for this chat/branch.</summary>
    IReadOnlyList<AI.Contracts.Resources.ChatResource> GetComposerResourceDraft(string draftKey);

    /// <summary>Resource changes are infrequent, so persist them immediately.</summary>
    Task SetComposerResourceDraftAsync(string draftKey,
        IReadOnlyList<AI.Contracts.Resources.ChatResource> references);

    /// <summary>
    /// Schedules a debounced (500ms) save of the draft. Skips scheduling entirely if the text
    /// already matches what's saved — typing back to an unchanged value costs nothing.
    /// </summary>
    void QueueComposerDraftSave(string draftKey, string text);

    /// <summary>
    /// Cancels any pending debounced save and writes the drafts dictionary to localStorage
    /// immediately. Call before navigating away from the current draft key (e.g. switching
    /// projects) so an unsent message doesn't get cancelled by the next project typing into
    /// the same single-debouncer and silently dropped.
    /// </summary>
    Task FlushPendingComposerDraftAsync();
}
