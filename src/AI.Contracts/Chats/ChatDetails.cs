namespace AI.Contracts.Chats;

using System.Text.Json;

public sealed record ChatDetails(
    Guid Id,
    Guid ProjectId,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision,
    Guid? ConnectionId,
    IReadOnlyList<ChatMessageView> Messages,
    IReadOnlyList<ChatBranchView>? Branches = null,
    IReadOnlyList<AI.Contracts.Projects.ToolPolicySettings>? ToolPolicies = null,
    bool AutoTitlePending = false,
    DateTimeOffset? ArchivedAt = null,
    Guid? ArchiveOperationId = null,
    ToolApprovalMode ApprovalMode = ToolApprovalMode.Ask, string Kind = "conversation",
    JsonElement? KindState = null, string InteractionSurface = "chat", bool AllowChatNavigation = true,
    bool ShowInMainRuns = true, int KindStateVersion = 1)
{
    /// <summary>The teammate a branch of this chat belongs to, when it is a team member's branch.</summary>
    public TeamMember? MemberOf(Guid branchId) => Branches?.SingleOrDefault(branch => branch.Id == branchId)?.Member;

    /// <summary>The branch is the main branch of a chat that runs a team: the top lead, which has no identity.</summary>
    public bool IsTeamLead(Guid branchId) => branchId == Id && Branches?.Any(branch => branch.Member is not null) == true;

    /// <summary>
    /// How a sender is named wherever this chat shows it: a teammate by its identity, the main
    /// branch of a team as the lead, any other branch by its title.
    /// </summary>
    public string SenderName(MessageSender sender) =>
        sender.ChatId != Id ? "Another chat"
        : MemberOf(sender.BranchId) is { } member ? member.Label
        : sender.BranchId == Id ? IsTeamLead(sender.BranchId) ? "Lead" : "Main branch"
        : Branches?.SingleOrDefault(branch => branch.Id == sender.BranchId)?.Title is { Length: > 0 } title ? title
        : "A branch";
}
