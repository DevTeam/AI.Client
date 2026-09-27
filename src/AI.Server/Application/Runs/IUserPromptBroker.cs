namespace AI.Application.Runs;

using Contracts.Runs;
using Tools;

/// <summary>
/// What a tool wants to ask, before the run has given it an identity. There is no tool call id
/// here because MCP has none to give: the id the model used to name its call never crosses the
/// protocol, so a prompt is identified by its own and shown for as long as the call runs.
/// </summary>
public sealed record UserPromptRequest(IReadOnlyList<UserPromptQuestion> Questions);

/// <summary>
/// Puts a question to the person watching a run and waits for their answer.
/// </summary>
/// <remarks>
/// Implemented by the run dispatcher, which is the only thing that knows whether a run is live and
/// has somewhere to show a card. A tool asks through this rather than reaching for the dispatcher
/// itself, so what a tool can do to a run stays one method wide.
///
/// A run that cannot be reached — not running, already stopping, or never interactive in the first
/// place — is not an error. It answers <see cref="UserPromptOutcome.Interrupted"/> straight away,
/// because a tool waiting on a person who does not exist is the one outcome worth ruling out.
/// </remarks>
public interface IUserPromptBroker
{
    Task<UserPromptResponse> AskAsync(
        ToolRunContext run,
        UserPromptRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
