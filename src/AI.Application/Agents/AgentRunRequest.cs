namespace AI.Application.Agents;

using Chat;

/// <summary>One agent turn on a branch: what to send and how the turn may behave.</summary>
/// <param name="Interactive">
/// Whether a person can be reached from this run. A background run says so once here rather than
/// refusing tool by tool, because "nobody is watching" is a property of the run.
/// </param>
/// <param name="ParentBranchId">
/// The branch of the run that delegated this one, when one did. It buys nothing for the run itself:
/// it is what lets the delegating turn report a file total that includes this work.
/// </param>
/// <param name="MaxModelSteps">
/// Background delegation is deliberately bounded: a narrow task that still needs dozens of model round
/// trips is exploring rather than executing. Null keeps ordinary chats unlimited.
/// </param>
/// <param name="SubtaskDepth">How many delegations started this run: 0 for a chat's own turn.</param>
public sealed record AgentRunRequest(
    Guid ProjectId,
    Guid ChatId,
    Guid BranchId,
    ChatCompletionRequest Completion,
    bool Interactive = true,
    Guid? ParentBranchId = null,
    bool OverlayPromptsAllowed = true,
    int? MaxModelSteps = null,
    int SubtaskDepth = 0);
