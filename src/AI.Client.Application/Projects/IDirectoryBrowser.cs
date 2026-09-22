namespace AI.Client.Application.Projects;

using AI.Client.Contracts.FileSystem;

/// <summary>
/// Read-only navigation of the host's own file system, so a directory grant can be picked instead
/// of typed. It lives next to the project service because that is the only thing that needs it:
/// a grant's <c>CanonicalRoot</c> is a path on the machine the tools run on, which is this one —
/// the browser the UI runs in cannot name that path, only the host can.
/// </summary>
public interface IDirectoryBrowser
{
    /// <summary>The level above every path: drives on Windows, "/" elsewhere.</summary>
    Task<DirectoryListing> ListRootsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The subdirectories of <paramref name="path"/>, or null if it is not a directory this host
    /// can list at all. A directory that exists but refuses to be opened is an empty listing, not
    /// a null one — the difference is "nothing to show" versus "no such place".
    /// </summary>
    Task<DirectoryListing?> ListAsync(string path, CancellationToken cancellationToken);

    /// <summary>Resolves a typed or pasted path to the form a grant would store, and says whether it exists.</summary>
    Task<DirectoryProbe> ResolveAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// The form a path is stored in. Two spellings of one directory must not become two grants,
    /// so every path that reaches storage passes through here first.
    /// </summary>
    string Canonicalize(string path);
}
