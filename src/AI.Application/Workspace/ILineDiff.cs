namespace AI.Application.Workspace;

/// <summary>
/// Line-level diff between two file versions: the number of lines added and removed net, plus
/// a unified diff of the same comparison. Goes through an interface so the workspace change
/// tracker can be tested against a fixture diff rather than the real Myers' implementation.
/// </summary>
public interface ILineDiff
{
    LineDiff.Result Compare(string? before, string? after);
}
