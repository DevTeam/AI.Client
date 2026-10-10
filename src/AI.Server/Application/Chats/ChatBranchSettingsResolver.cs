namespace AI.Application.Chats;

using AI.Contracts.Chats;
using AI.Contracts.Projects;

public interface IChatBranchSettingsResolver
{
    Guid? ConnectionId(ChatDetails chat, Guid branchId);
    ToolApprovalMode ApprovalMode(ChatDetails chat, Guid branchId);
    ToolPolicySettings? ToolPolicy(ChatDetails chat, Guid branchId, Guid serverId, string name, string schemaHash);
    IReadOnlyList<ToolPolicySettings> BranchToolPolicies(ChatDetails chat, Guid branchId, Guid serverId, string name, string schemaHash);

    /// <summary>The settings a branch runs with and, for each, the branch or chat that sets it.</summary>
    EffectiveBranchSettings Effective(ChatDetails chat, Guid branchId);
}

/// <param name="ConnectionFrom">"this branch", a branch title in quotes, or "chat" (which falls back to the project).</param>
/// <param name="ToolPolicyOverrides">How many tool policies this branch sets itself.</param>
public sealed record EffectiveBranchSettings(Guid BranchId, Guid? ConnectionId, string ConnectionFrom,
    ToolApprovalMode ApprovalMode, string ApprovalModeFrom, int ToolPolicyOverrides);

public sealed class ChatBranchSettingsResolver : IChatBranchSettingsResolver
{
    public Guid? ConnectionId(ChatDetails chat, Guid branchId) =>
        Lineage(chat, branchId).Select(branch => branch.Settings?.ConnectionId).OfType<Guid>().FirstOrDefault() is { } id && id != Guid.Empty
            ? id : chat.ConnectionId;

    public ToolApprovalMode ApprovalMode(ChatDetails chat, Guid branchId) =>
        Lineage(chat, branchId).Select(branch => branch.Settings?.ApprovalMode)
            .FirstOrDefault(mode => mode is not null) ?? chat.ApprovalMode;

    public ToolPolicySettings? ToolPolicy(ChatDetails chat, Guid branchId, Guid serverId, string name, string schemaHash) =>
        BranchToolPolicies(chat, branchId, serverId, name, schemaHash) is { Count: > 0 } policies
            ? policies[0]
            : chat.ToolPolicies?.FirstOrDefault(policy => policy.ServerId == serverId && policy.Name == name
                && policy.SchemaHash == schemaHash);

    public IReadOnlyList<ToolPolicySettings> BranchToolPolicies(ChatDetails chat, Guid branchId, Guid serverId, string name, string schemaHash) =>
        Lineage(chat, branchId).SelectMany(branch => branch.Settings?.ToolPolicies ?? [])
            .Where(policy => policy.ServerId == serverId && policy.Name == name && policy.SchemaHash == schemaHash)
            .ToArray();

    public EffectiveBranchSettings Effective(ChatDetails chat, Guid branchId)
    {
        var lineage = Lineage(chat, branchId).ToArray();
        var connection = lineage.FirstOrDefault(branch => branch.Settings?.ConnectionId is not null);
        var approval = lineage.FirstOrDefault(branch => branch.Settings?.ApprovalMode is not null);
        return new EffectiveBranchSettings(branchId, ConnectionId(chat, branchId), Source(connection, branchId),
            ApprovalMode(chat, branchId), Source(approval, branchId),
            lineage.FirstOrDefault(branch => branch.Id == branchId)?.Settings?.ToolPolicies?.Count ?? 0);
    }

    private static string Source(ChatBranchView? branch, Guid branchId) =>
        branch is null ? "chat" : branch.Id == branchId ? "this branch" : $"branch \"{branch.Title}\"";

    private static IEnumerable<ChatBranchView> Lineage(ChatDetails chat, Guid branchId)
    {
        var branches = (chat.Branches ?? []).ToDictionary(branch => branch.Id);
        var visited = new HashSet<Guid>();
        while (branchId != chat.Id && visited.Add(branchId) && branches.TryGetValue(branchId, out var branch))
        {
            yield return branch;
            branchId = branch.ParentBranchId ?? chat.Id;
        }
    }
}
