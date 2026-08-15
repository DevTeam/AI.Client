using AI.Client.Contracts.Chats;
using AI.Client.Contracts.Runs;

namespace AI.Client.Web.Composer;

/// <summary>
/// Snapshot of the composer + chat state at the moment the user pressed Enter. Passed to the
/// composer service so the decision logic stays independent from the Razor component's fields.
/// </summary>
public sealed record ComposerSubmitRequest(
    ComposerSubmitMode Mode,
    Guid? ProjectId,
    ChatDetails? SelectedChat,
    Guid? BranchLeafId,
    Guid? ForkSourceId,
    Guid? ReplaceSourceId,
    /// <summary>
    /// Whether the branch <see cref="ReplaceSourceId"/> is being replaced on is currently
    /// Generating. Computed by the caller (it depends on live run-stream state the service
    /// doesn't hold) and re-checked here so a race between opening the edit box and pressing
    /// Enter can't delete a branch out from under an in-flight response.
    /// </summary>
    bool ReplaceSourceIsGenerating,
    Guid? CredentialProfileId,
    string? EndpointBaseUrl,
    string? EndpointModel,
    string Message,
    ChatRunSnapshot? SelectedRun);
