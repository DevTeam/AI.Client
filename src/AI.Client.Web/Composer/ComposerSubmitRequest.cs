// ReSharper disable NotAccessedPositionalProperty.Global
namespace AI.Client.Web.Composer;

using AI.Client.Contracts.Chats;
using AI.Client.Contracts.Runs;

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
    Guid? CredentialProfileId,
    string? EndpointBaseUrl,
    string? EndpointModel,
    string Message,
    ChatRunSnapshot? SelectedRun,
    Guid? SelectedBranchId = null,
    IReadOnlyList<AI.Client.Contracts.Resources.ChatResourceRef>? Resources = null);
