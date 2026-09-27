namespace AI.Contracts.Workspace;

/// <summary>
/// Parses a unified-diff string into the typed lines the transcript renders. The parser is
/// stateless, but going through an interface lets the workspace view substitute one for tests
/// that want a fixture diff.
/// </summary>
public interface IUnifiedDiffParser
{
    IReadOnlyList<DiffLine> Parse(string? diff);
}
