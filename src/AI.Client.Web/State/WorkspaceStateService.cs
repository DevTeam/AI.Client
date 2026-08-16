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

        _draftSaveCts?.Cancel();
        _draftSaveCts?.Dispose();
        var cts = new CancellationTokenSource();
        _draftSaveCts = cts;
        _ = SaveDraftAfterDelayAsync(draftKey, text, cts.Token);
    }

    private async Task SaveDraftAfterDelayAsync(string key, string text, CancellationToken token)
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

        if (string.IsNullOrEmpty(text)) _composerDrafts.Remove(key); else _composerDrafts[key] = text;
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", token, ComposerDraftKey, JsonSerializer.Serialize(_composerDrafts));
    }

    public ValueTask DisposeAsync()
    {
        _draftSaveCts?.Cancel();
        _draftSaveCts?.Dispose();
        return ValueTask.CompletedTask;
    }
}
