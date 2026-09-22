namespace AI.Client.Web.FileSystem;

using AI.Client.Contracts.FileSystem;

public sealed class DirectoryPickerState(IFileSystemApi api) : IDirectoryPickerState
{
    // Clicks arrive faster than a slow share answers, and the answers come back out of order.
    // Only the newest navigation may write to the state; an older one that finishes late is
    // dropped, so the list never jumps back to a folder the user already left.
    private int _operation;

    public DirectoryListing? Listing { get; private set; }

    public string PathText { get; private set; } = string.Empty;

    public string Filter { get; private set; } = string.Empty;

    public bool IsLoading { get; private set; }

    public bool IsUnavailable { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? WarningMessage { get; private set; }

    /// <summary>
    /// A path the user typed that resolves fine but is not there. Granting a directory that does
    /// not exist yet is legitimate — the project it belongs to may be cloned after the grant — so
    /// this is offered with a warning rather than refused.
    /// </summary>
    private string? _missingPath;

    public string? Selection => _missingPath ?? (Listing is { CurrentPath.Length: > 0 } listing ? listing.CurrentPath : null);

    public bool PathTextNamesSelection =>
        Selection is { } selection && string.Equals(PathText.Trim(), selection, StringComparison.OrdinalIgnoreCase);

    public bool CanGoUp => Listing?.ParentPath is not null;

    public IReadOnlyList<DirectoryEntry> VisibleDirectories =>
        Listing is not { } listing ? []
        : Filter.Length == 0 ? listing.Directories
        : listing.Directories.Where(item => item.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase)).ToArray();

    public async Task OpenAsync(string? startPath, CancellationToken cancellationToken)
    {
        Listing = null;
        PathText = string.Empty;
        Filter = string.Empty;
        ErrorMessage = null;
        WarningMessage = null;
        _missingPath = null;
        IsUnavailable = false;

        // A remembered starting directory can have been deleted since the last time. That is not
        // worth an error — the picker opens at the drives instead, which is where it would have
        // opened had nothing been remembered.
        if (!string.IsNullOrWhiteSpace(startPath) && await LoadAsync(startPath, cancellationToken))
        {
            return;
        }

        await LoadRootsAsync(cancellationToken);
    }

    public async Task NavigateAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(path))
        {
            await LoadRootsAsync(cancellationToken);
            return;
        }

        if (!await LoadAsync(path, cancellationToken))
        {
            ErrorMessage = $"'{path}' could not be opened.";
        }
    }

    public Task GoUpAsync(CancellationToken cancellationToken) =>
        Listing?.ParentPath is { } parent ? NavigateAsync(parent, cancellationToken) : Task.CompletedTask;

    public async Task GoToTypedPathAsync(CancellationToken cancellationToken)
    {
        var typed = PathText.Trim();
        if (typed.Length == 0)
        {
            await LoadRootsAsync(cancellationToken);
            return;
        }

        if (await LoadAsync(typed, cancellationToken))
        {
            return;
        }

        var probe = await api.ResolveAsync(typed, cancellationToken);
        if (probe is null || probe.CanonicalPath.Length == 0)
        {
            ErrorMessage = $"'{typed}' could not be opened.";
            return;
        }

        // A grant over a relative root is dropped by the tool server without a word, so the path
        // has to be refused here, while the person who typed it is still looking at it.
        if (!probe.IsFullyQualified)
        {
            ErrorMessage = $"'{typed}' is not a full path. Start from a drive or root.";
            return;
        }

        _missingPath = probe.CanonicalPath;
        PathText = probe.CanonicalPath;
        ErrorMessage = null;
        WarningMessage = $"'{probe.CanonicalPath}' does not exist yet. It can still be granted.";
    }

    public void SetPathText(string value) => PathText = value;

    public void SetFilter(string value) => Filter = value;

    private async Task LoadRootsAsync(CancellationToken cancellationToken)
    {
        var operation = ++_operation;
        IsLoading = true;
        try
        {
            var roots = await api.ListRootsAsync(cancellationToken);
            if (operation != _operation) return;
            if (roots is null)
            {
                IsUnavailable = true;
                return;
            }

            Apply(roots);
        }
        catch (HttpRequestException)
        {
            if (operation == _operation) ErrorMessage = "The host could not be reached.";
        }
        finally
        {
            if (operation == _operation) IsLoading = false;
        }
    }

    private async Task<bool> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var operation = ++_operation;
        IsLoading = true;
        try
        {
            var listing = await api.ListAsync(path, cancellationToken);
            // A newer navigation already owns the state; this answer is reported as handled so the
            // caller does not start a third one on top of the two already in flight.
            if (operation != _operation) return true;
            if (listing is null) return false;
            Apply(listing);
            return true;
        }
        catch (HttpRequestException)
        {
            if (operation == _operation) ErrorMessage = "The host could not be reached.";
            return true;
        }
        finally
        {
            if (operation == _operation) IsLoading = false;
        }
    }

    private void Apply(DirectoryListing listing)
    {
        Listing = listing;
        PathText = listing.CurrentPath;
        Filter = string.Empty;
        _missingPath = null;
        ErrorMessage = null;
        WarningMessage = listing.IsAccessible ? null : $"'{listing.CurrentPath}' cannot be opened, so its contents are not shown.";
    }
}
