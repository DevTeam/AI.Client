namespace AI.Application.Agents;

using AI.Contracts.Runs;
using Chat;
using Tools;

/// <summary>
/// Where a harness reports a turn as it happens, and the two things it has to ask of whoever runs it.
/// The members with a body are optional: a caller that does not show drafts, context usage or asides
/// leaves them as they are.
/// </summary>
public interface IAgentRunSink
{
    /// <summary>A message the turn produced — a step with its tool calls, or a tool result — to record.</summary>
    Task PersistAsync(ChatCompletionMessage message, CancellationToken cancellationToken);

    /// <summary>The turn's answer text.</summary>
    Task TextAsync(string text, CancellationToken cancellationToken);

    /// <summary>The tool call running now, or <c>null</c> once none is.</summary>
    Task ActivityAsync(ToolActivity? activity, CancellationToken cancellationToken);

    /// <summary>The model endpoint is being waited for before a retry, or <c>null</c> once it is not.</summary>
    Task TransportAsync(ChatTransportWait? wait, CancellationToken cancellationToken);

    /// <summary>Whether a tool call that needs approval may run.</summary>
    Task<ToolApprovalAction> ApproveAsync(AgentTool tool, string arguments, long timeoutMilliseconds,
        ToolCallPosition position, CancellationToken cancellationToken);

    /// <summary>
    /// The prose of the model step in flight, chunk by chunk; <c>null</c> starts a new step. A live view
    /// only: what survives is whatever the step turns into.
    /// </summary>
    Task DraftAsync(string? content, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// How each model request fills the context window, and once more with the final answer counted.
    /// </summary>
    Task ContextUsageAsync(ContextUsage usage, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// The tool the step in flight has started to call, named before its arguments are complete. The
    /// completion protocol's own tool is not reported: it is how the run ends, not work.
    /// </summary>
    Task DraftToolCallAsync(string name, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Messages added to the branch while the turn runs, already persisted by the caller. Asked for after
    /// every tool batch, the only point where a user message keeps the history valid.
    /// </summary>
    Task<IReadOnlyList<ChatCompletionMessage>> TakeAsidesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ChatCompletionMessage>>([]);

    /// <summary>
    /// The turn ended because its steps stopped producing anything new; the report is the account the
    /// model gave of it, which is also the turn's answer.
    /// </summary>
    Task StalledAsync(string report, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Cancelled once a message is waiting to join the turn, so a wait in flight ends and the message is
    /// read now rather than when the wait runs out.
    /// </summary>
    CancellationToken JoinSignal => CancellationToken.None;
}
