namespace AI.Client.Contracts.Projects;

public sealed record DirectoryGrantSettings(
    Guid Id,
    string DisplayName,
    string CanonicalRoot,
    bool Recursive,
    IReadOnlyList<string> ToolNames);
