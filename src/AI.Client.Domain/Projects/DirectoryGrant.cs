namespace AI.Client.Domain.Projects;

using Common;

public sealed class DirectoryGrant
{
    private readonly HashSet<string> _toolNames;

    public DirectoryGrant(
        DirectoryGrantId id,
        string displayName,
        string canonicalRoot,
        bool recursive,
        IEnumerable<string> toolNames)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainException("Directory grant display name cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(canonicalRoot))
        {
            throw new DomainException("Directory grant root cannot be empty.");
        }

        ArgumentNullException.ThrowIfNull(toolNames);

        _toolNames = new HashSet<string>(
            toolNames.Select(i => i.Trim()).Where(i => i.Length > 0),
            StringComparer.Ordinal);

        if (_toolNames.Count == 0)
        {
            throw new DomainException("Directory grant must allow at least one tool.");
        }

        Id = id;
        DisplayName = displayName.Trim();
        CanonicalRoot = canonicalRoot.Trim();
        Recursive = recursive;
    }

    public DirectoryGrantId Id { get; }

    public string DisplayName { get; }

    public string CanonicalRoot { get; }

    public bool Recursive { get; }

    public IReadOnlySet<string> ToolNames => _toolNames;
}
