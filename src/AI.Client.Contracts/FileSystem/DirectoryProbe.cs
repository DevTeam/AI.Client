namespace AI.Client.Contracts.FileSystem;

/// <summary>
/// What the host makes of a path somebody typed or pasted.
/// </summary>
/// <param name="CanonicalPath">The form a grant would store it in.</param>
/// <param name="Exists">
/// Whether anything is there right now. A grant may legitimately name a directory that does not
/// exist yet, so absence is reported rather than rejected.
/// </param>
/// <param name="IsFullyQualified">
/// False for a path that names no root — "src\app" rather than "C:\src\app". The tool server drops
/// such a grant on sight, so it has to be caught while the user is still looking at it.
/// </param>
public sealed record DirectoryProbe(string CanonicalPath, bool Exists, bool IsFullyQualified);
