namespace AI.Web.Pages;

using AI.Contracts.Resources;

public partial class Home
{
    /// <summary>
    /// A reference the Host refused because no read grant of the project covers it, kept with
    /// everything needed to add it again once access is allowed.
    /// </summary>
    /// <param name="Folder">The directory a grant would cover, or null when that would be a whole drive.</param>
    private sealed record PendingResourceAccess(string DraftKey, ChatResourceKind Kind, string Path, string? Folder,
        ChatLineRange? Lines, string? Mention, bool IncludeContent);

    private PendingResourceAccess? _pendingResourceAccess;
    private bool _isGrantingResourceAccess;

    /// <summary>The request waiting for access in the composer that is open now.</summary>
    private PendingResourceAccess? GetPendingResourceAccess() =>
        _pendingResourceAccess is { } pending && pending.DraftKey == GetComposerDraftKey() ? pending : null;

    /// <summary>
    /// Asks the Host why <paramref name="path"/> was refused. When it exists but the project cannot
    /// read it, the composer offers to allow access instead of showing the refusal.
    /// </summary>
    private async Task<bool> OfferResourceAccessAsync(Guid projectId, string draftKey, string path, ChatLineRange? lines,
        string? mention, bool includeContent)
    {
        IReadOnlyList<ResolvedPath> resolved;
        try
        {
            resolved = await ResourceApi.ResolveAsync(projectId, [path], CancellationToken.None);
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException)
        {
            return false;
        }
        if (resolved is not [{ Path: { } found, Kind: { } kind, Access: PathAccess.None } item]) return false;
        if (_selectedProject?.Id != projectId || GetComposerDraftKey() != draftKey) return true;
        var plan = DropAccessPlanner.Plan([item]);
        _pendingResourceAccess = new PendingResourceAccess(draftKey, kind, found, plan.Folders.Count > 0 ? plan.Folders[0] : null,
            kind == ChatResourceKind.File ? lines : null, mention, includeContent && kind == ChatResourceKind.File);
        _chatError = null;
        return true;
    }

    private void DismissResourceAccess() => _pendingResourceAccess = null;

    /// <summary>
    /// Grants the project read access to the folder, subfolders included, the way a drop does,
    /// then adds the reference the Host refused.
    /// </summary>
    private async Task AllowResourceAccessAsync()
    {
        if (GetPendingResourceAccess() is not { Folder: { } folder } pending || _isGrantingResourceAccess) return;
        _isGrantingResourceAccess = true;
        try
        {
            if (!await GrantDroppedFoldersAsync([folder]))
            {
                Notifications.ShowError($"Access to {GetDirectoryDisplayNameSuggestion(folder)} could not be saved. Reload the project and try again.");
                return;
            }
            _pendingResourceAccess = null;
            await AddDraftResourceAsync([pending.Kind], pending.Path, lines: pending.Lines, mention: pending.Mention,
                includeContent: pending.IncludeContent, offerAccess: false);
            if (_chatError is null)
                Notifications.ShowSuccess($"The project can now read {folder} and its subfolders.");
        }
        finally
        {
            _isGrantingResourceAccess = false;
        }
    }
}
