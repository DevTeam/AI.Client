namespace AI.Client.Web.State;

using Microsoft.JSInterop;
using System.Text.Json;

public sealed class WorkspaceStateService(IJSRuntime jsRuntime) : IWorkspaceStateService
{
    private const string LastProjectKey = "ai-client.last-project.v1";
    private const string ProjectContextKey = "ai-client.project-context.v1";
    private const string ComposerDraftKey = "ai-client.composer-drafts.v1";

    // 500ms after the last keystroke, not on every input: a save is a JSInterop call plus a
    // JsonSerializer.Serialize of the whole drafts dictionary, and the composer's oninput already
    // re-renders the message list on every keystroke (Home.razor/MessageFeed.razor) — adding a
    // synchronous localStorage write on top of that would reintroduce typing latency that was
    // deliberately removed elsewhere in this app.
    private static readonly TimeSpan DraftSaveDelay = TimeSpan.FromMilliseconds(500);

    private sealed record ProjectContextEntry(Guid? ChatId, Guid? BranchLeafId);

    // One entry per project/chat rather than a single "last used" slot: switching project A -> B
    // -> A must not lose A's own context, and a chat can be mid-draft in more than one place at once.
    private Dictionary<Guid, ProjectContextEntry> _projectContexts = [];
    private Dictionary<string, string> _composerDrafts = [];
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

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        var lastProjectRaw = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", LastProjectKey);
        LastProjectId = Guid.TryParse(lastProjectRaw, out var id) ? id : null;

        _projectContexts = await LoadDictionaryAsync<Guid, ProjectContextEntry>(ProjectContextKey);
        _composerDrafts = await LoadDictionaryAsync<string, string>(ComposerDraftKey);
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

    public (Guid? ChatId, Guid? BranchLeafId) GetProjectContext(Guid projectId) =>
        _projectContexts.TryGetValue(projectId, out var entry) ? (entry.ChatId, entry.BranchLeafId) : (null, null);

    public async Task SetProjectContextAsync(Guid projectId, Guid? chatId, Guid? branchLeafId)
    {
        var next = new ProjectContextEntry(chatId, branchLeafId);
        if (_projectContexts.TryGetValue(projectId, out var current) && current == next) return;
        _projectContexts[projectId] = next;
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", ProjectContextKey, JsonSerializer.Serialize(_projectContexts));
    }

    public string GetComposerDraft(string draftKey) =>
        draftKey.Length == 0 ? string.Empty : _composerDrafts.GetValueOrDefault(draftKey, string.Empty);

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

        await jsRuntime.InvokeVoidAsync("localStorage.setItem", token, ComposerDraftKey, JsonSerializer.Serialize(_composerDrafts));
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

    public ValueTask DisposeAsync()
    {
        _draftSaveCts?.Cancel();
        _draftSaveCts?.Dispose();
        return ValueTask.CompletedTask;
    }
}
