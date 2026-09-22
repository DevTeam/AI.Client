namespace AI.Client.Web.FileSystem;

using AI.Client.Contracts.FileSystem;

/// <summary>
/// Everything the directory picker knows: where it is, what it can show, and what pressing
/// "Select" would hand back. It is a plain object rather than fields on the dialog component so
/// the navigation rules — which are the part with corners in them — can be tested without a
/// renderer.
/// </summary>
public interface IDirectoryPickerState
{
    /// <summary>The level currently shown, or null before the first load finishes.</summary>
    DirectoryListing? Listing { get; }

    /// <summary>The contents of <see cref="Listing"/> narrowed by <see cref="Filter"/>.</summary>
    IReadOnlyList<DirectoryEntry> VisibleDirectories { get; }

    /// <summary>What the path box holds. Follows navigation, and can be typed into.</summary>
    string PathText { get; }

    string Filter { get; }

    bool IsLoading { get; }

    /// <summary>True when the host does not offer file-system browsing at all.</summary>
    bool IsUnavailable { get; }

    string? ErrorMessage { get; }

    /// <summary>Said out loud when a path is selectable but not there yet.</summary>
    string? WarningMessage { get; }

    /// <summary>The path "Select" would return, or null while there is nothing to select.</summary>
    string? Selection { get; }

    /// <summary>
    /// True when the path box already names what "Select" would return — nothing new has been
    /// typed into it. Enter then means "take this one" rather than "go there"; there is nowhere
    /// left to go.
    /// </summary>
    bool PathTextNamesSelection { get; }

    bool CanGoUp { get; }

    Task OpenAsync(string? startPath, CancellationToken cancellationToken);

    Task NavigateAsync(string path, CancellationToken cancellationToken);

    Task GoUpAsync(CancellationToken cancellationToken);

    /// <summary>Goes to whatever is in the path box — how a pasted path gets you there.</summary>
    Task GoToTypedPathAsync(CancellationToken cancellationToken);

    void SetPathText(string value);

    void SetFilter(string value);
}
