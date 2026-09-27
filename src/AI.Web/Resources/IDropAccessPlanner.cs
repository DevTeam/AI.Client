namespace AI.Web.Resources;

using AI.Contracts.Resources;

/// <summary>
/// Decides which directories to grant so that dropped files outside the project become readable.
/// </summary>
public interface IDropAccessPlanner
{
    /// <param name="blocked">Existing paths the project cannot read yet.</param>
    DropAccessPlan Plan(IReadOnlyList<ResolvedPath> blocked);
}

/// <param name="Folders">Directories to grant, each with its subdirectories.</param>
/// <param name="Refused">Items that would need a whole drive or file system root; nothing is granted for them.</param>
public sealed record DropAccessPlan(IReadOnlyList<string> Folders, IReadOnlyList<ResolvedPath> Refused);
