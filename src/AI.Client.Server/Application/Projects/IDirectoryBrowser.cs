namespace AI.Client.Application.Projects;

using AI.Client.Contracts.FileSystem;

/// <summary>
/// Read-only navigation of the host's own file system, so a path can be picked instead of typed —
/// a directory grant's root, or an answer to a question the model asked. It lives here because a
/// grant's <c>CanonicalRoot</c> is a path on the machine the tools run on, which is this one: the
/// browser the UI runs in cannot name that path, only the host can.
/// </summary>
public interface IDirectoryBrowser
{
    /// <summary>The level above every path: drives on Windows, "/" elsewhere.</summary>
    Task<DirectoryListing> ListRootsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The contents of <paramref name="path"/>, or null if it is not a directory this host can list
    /// at all. A directory that exists but refuses to be opened is an empty listing, not a null one
    /// — the difference is "nothing to show" versus "no such place". Files are left out unless
    /// <paramref name="includeFiles"/> asks for them, because most of the time they are noise
    /// between the caller and the folder they are heading for.
    /// </summary>
    Task<DirectoryListing?> ListAsync(string path, bool includeFiles, CancellationToken cancellationToken);

    /// <summary>Resolves a typed or pasted path to its stored form, and says what is there.</summary>
    Task<DirectoryProbe> ResolveAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// The form a path is stored in. Two spellings of one directory must not become two grants,
    /// so every path that reaches storage passes through here first.
    /// </summary>
    string Canonicalize(string path);
}
