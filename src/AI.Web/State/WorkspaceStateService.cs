namespace AI.Web.State;

using Microsoft.JSInterop;
using System.Text.Json;

public sealed class WorkspaceStateService(IJSRuntime jsRuntime) : IWorkspaceStateService
{
    private const string LastProjectKey = "ai-client.last-project.v1";
    private const string ProjectContextKey = "ai-client.project-context.v1";
    private const string ComposerDraftKey = "ai-client.composer-drafts.v1";
    private const string ComposerResourceDraftKey = "ai-client.composer-resource-drafts.v1";
    private const string ComposerHistoryKey = "ai-client.composer-history.v1";
    private const string LastBrowsedDirectoryKey = "ai-client.last-browsed-directory.v1";

    // Enough to reach anything a user would still recognise, small enough that the whole blob
    // stays cheap to serialize on every send.
    private const int MaxHistoryEntries = 100;

    // 500ms after the last keystroke, not on every input: a save is a JSInterop call plus a
    // JsonSerializer.Serialize of the whole drafts dictionary, and the composer's oninput already
    // re-renders the message list on every keystroke (Home.razor/MessageFeed.razor) — adding a
    // synchronous localStorage write on top of that would reintroduce typing latency that was
    // deliberately removed elsewhere in this app.
    private static readonly TimeSpan DraftSaveDelay = TimeSpan.FromMilliseconds(500);

    private sealed record ProjectContextEntry(Guid? ChatId, Guid? BranchLeafId, Guid? BranchId);

    // One entry per project/chat rather than a single "last used" slot: switching project A -> B
    // -> A must not lose A's own context, and a chat can be mid-draft in more than one place at once.
    private Dictionary<Guid, ProjectContextEntry> _projectContexts = [];
    private Dictionary<string, string> _composerDrafts = [];
    private Dictionary<string, AI.Contracts.Resources.ChatResource[]> _composerResourceDrafts = [];
    // Per project, newest first. Scoped to the project rather than the chat because the point of
    // the feature is re-sending a phrasing the user already used ("run the tests", "do the
    // recommended thing"), and that reuse happens across the project's chats — a per-chat history
    // would be empty exactly when a new chat needs it most.
    private Dictionary<Guid, List<string>> _composerHistory = [];
    private bool _initialized;
    // Tracks the last key/text that was queued but not yet persisted. Used by
    // FlushPendingComposerDraftAsync to know whether a write is actually owed. The text is
    // already reflected in _composerDrafts — we update that eagerly in QueueComposerDraftSave
    // so a draft for one key isn't silently dropped when a save for a different key cancels
    // the shared debouncer (see issue: typing in project A then switching to B then typing in
    // B within 500ms used to lose A's text entirely because the dictionary was only mutated
    // inside the now-cancelled SaveDraftAfterDelayAsync).
    private string? _pendingDraftKey;
    private CancellationTokenSource? _draftSaveCts;

    public Guid? LastProjectId { get; private set; }

    public string? LastBrowsedDirectory { get; private set; }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        var lastProjectRaw = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", LastProjectKey);
        LastProjectId = Guid.TryParse(lastProjectRaw, out var id) ? id : null;

        _projectContexts = await LoadDictionaryAsync<Guid, ProjectContextEntry>(ProjectContextKey);
        _composerDrafts = await LoadDictionaryAsync<string, string>(ComposerDraftKey);
        _composerResourceDrafts = await LoadDictionaryAsync<string, AI.Contracts.Resources.ChatResource[]>(ComposerResourceDraftKey);
        _composerHistory = await LoadDictionaryAsync<Guid, List<string>>(ComposerHistoryKey);
        LastBrowsedDirectory = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", LastBrowsedDirectoryKey);
    }

    private async Task<Dictionary<TKey, TValue>> LoadDictionaryAsync<TKey, TValue>(string key) where TKey : notnull
    {
        var json = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", key);
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<TKey, TValue>>(json) ?? [];
        }
        catch (JsonException)
        {
            // Malformed/stale blob (e.g. from an older schema) — start fresh rather than crash
            // the whole app over a UI-convenience cache.
            return [];
        }
    }

    public async Task SetLastProjectIdAsync(Guid projectId)
    {
        if (LastProjectId == projectId) return;
        LastProjectId = projectId;
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", LastProjectKey, projectId.ToString());
    }

    public async Task SetLastBrowsedDirectoryAsync(string path)
    {
        if (path.Length == 0 || LastBrowsedDirectory == path) return;
        LastBrowsedDirectory = path;
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", LastBrowsedDirectoryKey, path);
    }

    public (Guid? ChatId, Guid? BranchLeafId, Guid? BranchId) GetProjectContext(Guid projectId) =>
        _projectContexts.TryGetValue(projectId, out var entry)
            ? (entry.ChatId, entry.BranchLeafId, entry.BranchId) : (null, null, null);

    public async Task SetProjectContextAsync(Guid projectId, Guid? chatId, Guid? branchLeafId, Guid? branchId = null)
    {
        var next = new ProjectContextEntry(chatId, branchLeafId, chatId is null ? null : branchId);
        if (_projectContexts.TryGetValue(projectId, out var current) && current == next) return;
        _projectContexts[projectId] = next;
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", ProjectContextKey, JsonSerializer.Serialize(_projectContexts));
    }

    public string GetComposerDraft(string draftKey) =>
        draftKey.Length == 0 ? string.Empty : _composerDrafts.GetValueOrDefault(draftKey, string.Empty);

    public IReadOnlyList<AI.Contracts.Resources.ChatResource> GetComposerResourceDraft(string draftKey) =>
        draftKey.Length == 0 ? [] : _composerResourceDrafts.GetValueOrDefault(draftKey, []);

    public async Task SetComposerResourceDraftAsync(string draftKey,
        IReadOnlyList<AI.Contracts.Resources.ChatResource> references)
    {
        if (draftKey.Length == 0) return;
        if (references.Count == 0) _composerResourceDrafts.Remove(draftKey);
        else _composerResourceDrafts[draftKey] = references.ToArray();
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", ComposerResourceDraftKey,
            JsonSerializer.Serialize(_composerResourceDrafts));
    }

    public IReadOnlyList<string> GetComposerHistory(Guid projectId) =>
        _composerHistory.TryGetValue(projectId, out var entries) ? entries : [];

    public async Task AppendComposerHistoryAsync(Guid projectId, string text)
    {
        var entry = text.Trim();
        if (entry.Length == 0) return;

        var entries = _composerHistory.TryGetValue(projectId, out var existing) ? existing : _composerHistory[projectId] = [];
        // Move-to-front rather than plain append: re-sending the same phrasing is the whole
        // point of the feature, and leaving the old copy behind would make the user walk past
        // the same text several times to reach anything older.
        entries.RemoveAll(item => item == entry);
        entries.Insert(0, entry);
        if (entries.Count > MaxHistoryEntries) entries.RemoveRange(MaxHistoryEntries, entries.Count - MaxHistoryEntries);

        // Written straight through, not debounced like drafts: this runs once per send, not once
        // per keystroke, and losing the last sent message from history to a tab close would
        // defeat the point of persisting it at all.
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", ComposerHistoryKey, JsonSerializer.Serialize(_composerHistory));
    }

    public void QueueComposerDraftSave(string draftKey, string text)
    {
        if (draftKey.Length == 0) return;
        if (_composerDrafts.GetValueOrDefault(draftKey, string.Empty) == text) return;

        // Reflect the new value in the in-memory dictionary immediately, before scheduling the
        // debounce. The shared _draftSaveCts gets cancelled when the next keystroke arrives —
        // if the dictionary were only mutated inside SaveDraftAfterDelayAsync (as it used to
        // be), cancelling the in-flight write would also discard the previous key's text:
        // typing in project A, switching to B, then typing in B within 500ms would lose A's
        // draft entirely, because the cancelled A-task never got to set _composerDrafts["new:{A.Id}"].
        // Updating eagerly means the eventual write covers both keys regardless of who wins.
        if (string.IsNullOrEmpty(text)) _composerDrafts.Remove(draftKey); else _composerDrafts[draftKey] = text;
        _pendingDraftKey = draftKey;

        _draftSaveCts?.Cancel();
        _draftSaveCts?.Dispose();
        var cts = new CancellationTokenSource();
        _draftSaveCts = cts;
        _ = SaveDraftAfterDelayAsync(cts.Token);
    }

    private async Task SaveDraftAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(DraftSaveDelay, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (token.IsCancellationRequested) return;

        try
        {
            await jsRuntime.InvokeVoidAsync("localStorage.setItem", token, ComposerDraftKey, JsonSerializer.Serialize(_composerDrafts));
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke or a flush cancelled this write mid-interop and owns the draft
            // now. Nothing is lost — `_pendingDraftKey` stays set, so whoever cancelled still
            // owes the write — but the exception would otherwise escape a task nobody awaits.
            return;
        }

        if (!token.IsCancellationRequested) _pendingDraftKey = null;
    }

    public async Task FlushPendingComposerDraftAsync()
    {
        if (_pendingDraftKey is null) return;
        _draftSaveCts?.Cancel();
        _draftSaveCts?.Dispose();
        _draftSaveCts = null;
        // The dictionary is already up to date (QueueComposerDraftSave updates it eagerly);
        // the only thing left to do is push it to localStorage so the next page load — or a
        // tab close inside the 500ms debounce window — still sees the draft. Without this,
        // navigating from project A to B within the debounce window would only persist the
        // draft if the user typed something in B (cancelling A's timer) to trigger another
        // save; if the user just clicked around, the timer would still fire eventually, but
        // a quick tab close would race it.
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", ComposerDraftKey, JsonSerializer.Serialize(_composerDrafts));
        _pendingDraftKey = null;
    }

    public async ValueTask DisposeAsync()
    {
        // Cancelling alone would throw away a draft the debounce had not written yet — the last
        // few hundred milliseconds of typing before the workspace is torn down, which is the
        // very case this class exists to survive. Flush first, then cancel.
        try
        {
            await FlushPendingComposerDraftAsync();
        }
        catch (Exception error) when (error is JSException or JSDisconnectedException or ObjectDisposedException
                                          or OperationCanceledException or InvalidOperationException)
        {
            // The page is going away; there is no longer anywhere to write to, and failing to
            // save a draft must not fail disposal.
        }

        _draftSaveCts?.Cancel();
        _draftSaveCts?.Dispose();
        _draftSaveCts = null;
    }
}
