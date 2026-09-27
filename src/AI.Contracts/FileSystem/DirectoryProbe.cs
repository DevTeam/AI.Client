namespace AI.Contracts.FileSystem;

/// <summary>
/// What the host makes of a path somebody typed or pasted.
/// </summary>
/// <param name="CanonicalPath">The form a grant, or an answer, would store it in.</param>
/// <param name="IsFullyQualified">
/// False for a path that names no root — "src\app" rather than "C:\src\app". The tool server drops
/// a grant with such a root on sight, so it has to be caught while the user is still looking at it.
/// </param>
/// <param name="DirectoryExists">A directory is there right now.</param>
/// <param name="FileExists">
/// A file is there right now. Kept apart from <paramref name="DirectoryExists"/> because what is
/// being asked for decides which of the two is an answer and which is a mistake.
/// </param>
public sealed record DirectoryProbe(
    string CanonicalPath,
    bool IsFullyQualified,
    bool DirectoryExists,
    bool FileExists);
