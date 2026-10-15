namespace AI.Contracts.Runs;

/// <param name="CallIndex">Which call of the model's batch this is, counting from one.</param>
/// <param name="BatchSize">
/// How many calls that batch holds. Confirmations are asked one at a time on purpose — allowing one
/// call must never imply allowing its neighbours — but a user facing the first of several deserves
/// to know that before deciding.
/// </param>
/// <param name="Assessment">
/// What the chat's risk assessment recommends about this call in manual or automatic approval
/// mode; null when no usable assessment is available. The person decides while this card is shown.
/// </param>
/// <param name="Command">What the call runs, for a <c>process_run</c> call; null for any other tool.</param>
public sealed record ToolApproval(
    Guid Id,
    Guid ServerId,
    string Name,
    string SchemaHash,
    string Arguments,
    long TimeoutSeconds,
    int CallIndex = 1,
    int BatchSize = 1,
    string? Assessment = null,
    CommandApproval? Command = null);

/// <summary>The program a <c>process_run</c> call starts, as the approval card shows it.</summary>
/// <param name="Display">The command as a person reads it, such as <c>git -C . log --oneline</c>.</param>
/// <param name="Options">
/// Command patterns the card may remember instead of the whole tool, the one to preselect first.
/// Empty when only the whole tool can be remembered.
/// </param>
/// <param name="Warnings">What the person should know before allowing it, one sentence each.</param>
/// <param name="Rule">The pattern of the command rule that asks about this call, when one does.</param>
public sealed record CommandApproval(
    string Display,
    IReadOnlyList<string> Options,
    IReadOnlyList<string> Warnings,
    string? Rule = null);

public enum ToolApprovalAction
{
    Allow,
    AllowForChat,
    AllowForProject,
    AllowGlobally,
    Deny
}

/// <param name="Pattern">
/// For a remembering action on a <c>process_run</c> call: one of <see cref="CommandApproval.Options"/>,
/// saved as a command rule instead of a policy for the whole tool. Null remembers the whole tool.
/// </param>
public sealed record ToolApprovalDecision(Guid ApprovalId, ToolApprovalAction Action, string? Pattern = null);
