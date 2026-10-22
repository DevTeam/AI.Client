namespace AI.Application.Agents;

using AI.Contracts.Workspace;

/// <summary>
/// Runs one agent turn: streams the model's answer, runs the tool calls it asks for, reports both to the
/// sink, and says what the workspace looks like once the turn is over (ADR-014). The run's history,
/// state and UI consume the sink, so another harness can stand in without them changing.
/// </summary>
public interface IAgentHarness
{
    Task<WorkspaceChangeSet> RunAsync(AgentRunRequest request, IAgentRunSink sink, CancellationToken cancellationToken);
}
