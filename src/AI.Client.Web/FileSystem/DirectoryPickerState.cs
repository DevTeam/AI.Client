namespace AI.Client.Web.FileSystem;

using AI.Client.Contracts.FileSystem;

public sealed class DirectoryPickerState(IFileSystemApi api) : IDirectoryPickerState
{
    /// <summary>Both are separators here: the host decides which, and it may not be this one.</summary>
    private static readonly char[] Separators = ['\\', '/'];

    // Clicks arrive faster than a slow share answers, and the answers come back out of order.
    // Only the newest navigation may write to the state; an older one that finishes late is
    // dropped, so the list never jumps back to a folder the user already left.
    private int _operation;

    /// <summary>
    /// A path the user typed that resolves fine but is not there. Granting a directory that does
    /// not exist yet is legitimate — the project may be cloned after the grant — and so is naming
    /// a file that is about to be written, so this is offered with a warning rather than refused.
    /// </summary>
    private string? _missingPath;

    public DirectoryPickerMode Mode { get; private set; }

    public DirectoryListing? Listing { get; private set; }

    public string PathText { get; private set; } = string.Empty;

    public string Filter { get; private set; } = string.Empty;

    public bool IsLoading { get; private set; }

    public bool IsUnavailable { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? WarningMessage { get; private set; }

    public string? SelectedFile { get; private set; }

    /// <summary>
    /// In directory mode the folder you are standing in is the answer. In file mode standing in a
    /// folder is not an answer at all — a file has to be named — so the selection stays empty until
    /// one is clicked or typed.
    /// </summary>
    public string? Selection => Mode == DirectoryPickerMode.File
        ? SelectedFile ?? _missingPath
        : _missingPath ?? (Listing is { CurrentPath.Length: > 0 } listing ? listing.CurrentPath : null);

    public bool PathTextNamesSelection =>
        Selection is { } selection && string.Equals(PathText.Trim(), selection, StringComparison.OrdinalIgnoreCase);

    public bool CanGoUp => Listing?.ParentPath is not null;

    public IReadOnlyList<DirectoryEntry> VisibleDirectories => Visible(Listing?.Directories);

    // Directory mode does not ask for files, and does not show them if a host sends some anyway:
    // what is on screen is what can be picked, and there a file cannot be.
    public IReadOnlyList<DirectoryEntry> VisibleFiles =>
        Mode == DirectoryPickerMode.File ? Visible(Listing?.Files) : [];

    public async Task OpenAsync(string? startPath, DirectoryPickerMode mode, CancellationToken cancellationToken)
    {
        Mode = mode;
        Listing = null;
        PathText = string.Empty;
        Filter = string.Empty;
        ErrorMessage = null;
        WarningMessage = null;
        SelectedFile = null;
        _missingPath = null;
        IsUnavailable = false;

        // Being handed a file to start from is the normal way to reopen a question already answered:
        // the folder it lives in is where the walk resumes, with the file still picked.
        if (!string.IsNullOrWhiteSpace(startPath) && await StartAtAsync(startPath, cancellationToken))
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

    public void SelectFile(string path)
    {
        if (Mode != DirectoryPickerMode.File || string.IsNullOrEmpty(path)) return;
        SelectedFile = path;
        PathText = path;
        _missingPath = null;
        ErrorMessage = null;
        WarningMessage = null;
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

        // A directory answers for itself: going there is what was meant, in either mode.
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

        if (probe.FileExists)
        {
            if (Mode == DirectoryPickerMode.Directory)
            {
                ErrorMessage = $"'{probe.CanonicalPath}' is a file, not a directory.";
                return;
            }

            // Opening the folder around it is what makes the pick visible: a file named into the
            // box and then shown nowhere looks like nothing happened.
            await OpenParentOfAsync(probe.CanonicalPath, cancellationToken);
            SelectFile(probe.CanonicalPath);
            return;
        }

        _missingPath = probe.CanonicalPath;
        SelectedFile = null;
        PathText = probe.CanonicalPath;
        ErrorMessage = null;
        WarningMessage = Mode == DirectoryPickerMode.File
            ? $"'{probe.CanonicalPath}' does not exist yet. It can still be named."
            : $"'{probe.CanonicalPath}' does not exist yet. It can still be granted.";
    }

    public void SetPathText(string value) => PathText = value;

    public void SetFilter(string value) => Filter = value;

    private IReadOnlyList<DirectoryEntry> Visible(IReadOnlyList<DirectoryEntry>? entries) =>
        entries is null ? []
        : Filter.Length == 0 ? entries
        : entries.Where(item => item.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase)).ToArray();

    /// <summary>
    /// Opens the place a remembered path points at. A directory is opened; a file opens the folder
    /// around it and is picked. Anything else is a path that has gone, which is not worth an error:
    /// the picker falls back to the roots, where it would have opened had nothing been remembered.
    /// </summary>
    private async Task<bool> StartAtAsync(string startPath, CancellationToken cancellationToken)
    {
        if (await LoadAsync(startPath, cancellationToken))
        {
            return true;
        }

        if (Mode != DirectoryPickerMode.File) return false;

        var probe = await api.ResolveAsync(startPath, cancellationToken);
        if (probe is not { FileExists: true }) return false;
        if (!await OpenParentOfAsync(probe.CanonicalPath, cancellationToken)) return false;

        SelectFile(probe.CanonicalPath);
        return true;
    }

    private async Task<bool> OpenParentOfAsync(string path, CancellationToken cancellationToken)
    {
        var separator = path.LastIndexOfAny(Separators);
        return separator > 0 && await LoadAsync(path[..separator], cancellationToken);
    }

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
            var listing = await api.ListAsync(path, Mode == DirectoryPickerMode.File, cancellationToken);
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
        SelectedFile = null;
        _missingPath = null;
        ErrorMessage = null;
        WarningMessage = listing.IsAccessible ? null : $"'{listing.CurrentPath}' cannot be opened, so its contents are not shown.";
    }
}
