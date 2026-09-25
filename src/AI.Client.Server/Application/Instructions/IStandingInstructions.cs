namespace AI.Client.Application.Instructions;

using AI.Client.Contracts.Instructions;

/// <summary>
/// Builds the standing part of the system prompt — the base prompt, the project's instructions and
/// the memory index — each within its own token budget. The same result feeds a run and the
/// "what the model sees" preview, so the two cannot disagree.
/// </summary>
public interface IStandingInstructions
{
    /// <param name="appToolsAvailable">
    /// Whether the run can reach the App tools. Without them the memory layer does not tell the
    /// model about tools it cannot call.
    /// </param>
    Task<ModelContextPreview> BuildAsync(Guid projectId, bool appToolsAvailable, CancellationToken cancellationToken);
}
