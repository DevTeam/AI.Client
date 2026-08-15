namespace AI.Client.Web.State;

/// <summary>
/// Client-only "where was I" memory (last project, last chat/branch per project, unsent composer
/// drafts), persisted to localStorage. Unrelated to chat data itself, which the server already
/// owns — this is purely a UI convenience so the app reopens where it was left.
/// </summary>
public interface IWorkspaceStateService : IAsyncDisposable
{
    /// <summary>Loads everything from localStorage. Safe to call more than once; only the first call does any work.</summary>
    Task InitializeAsync();

    /// <summary>The project id last selected, or null if none was ever recorded.</summary>
    Guid? LastProjectId { get; }

    /// <summary>Records the given project as the last one selected. No-op (no write) if it's already the current value.</summary>
    Task SetLastProjectIdAsync(Guid projectId);

    /// <summary>The chat/branch last selected within the given project, if any.</summary>
    (Guid? ChatId, Guid? BranchLeafId) GetProjectContext(Guid projectId);

    /// <summary>Records the chat/branch last selected within the given project. No-op (no write) if unchanged.</summary>
    Task SetProjectContextAsync(Guid projectId, Guid? chatId, Guid? branchLeafId);

    /// <summary>The unsent composer draft for the given key (chat id, or "new:{projectId}" before the first message creates the chat).</summary>
    string GetComposerDraft(string draftKey);

    /// <summary>
    /// Schedules a debounced (500ms) save of the draft. Skips scheduling entirely if the text
    /// already matches what's saved — typing back to an unchanged value costs nothing.
    /// </summary>
    void QueueComposerDraftSave(string draftKey, string text);
}
