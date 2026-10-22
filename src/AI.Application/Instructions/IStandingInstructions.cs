namespace AI.Application.Instructions;

using AI.Contracts.Instructions;
using AI.Contracts.Settings;

/// <summary>
/// Collects the base prompt, project rules, memory and skills, then delegates their adaptive
/// projection to IAdaptiveContextPolicy. A run supplies its selected connection; the preview
/// uses the project/default connection when no override is supplied.
/// </summary>
public interface IStandingInstructions
{
    /// <param name="appToolsAvailable">
    /// Whether the run can reach the App tools. Without them the memory layer does not tell the
    /// model about tools it cannot call.
    /// </param>
    Task<ModelContextPreview> BuildAsync(Guid projectId, bool appToolsAvailable, CancellationToken cancellationToken,
        ConnectionSettings? connection = null);
}
