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
    /// <param name="skillsRunnable">
    /// Whether the run can start a skill: <c>run_skill</c> is not denied, and a person can approve it
    /// when it asks. Otherwise the skill catalog is left out rather than offering what cannot run.
    /// </param>
    Task<ModelContextPreview> BuildAsync(Guid projectId, bool appToolsAvailable, CancellationToken cancellationToken,
        ConnectionSettings? connection = null, bool skillsRunnable = true);
}
