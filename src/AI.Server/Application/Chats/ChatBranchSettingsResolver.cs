namespace AI.Application.Chats;

using AI.Contracts.Chats;
using AI.Contracts.Projects;

public interface IChatBranchSettingsResolver
{
    Guid? ConnectionId(ChatDetails chat, Guid branchId);
    ToolApprovalMode ApprovalMode(ChatDetails chat, Guid branchId);
    ToolPolicySettings? ToolPolicy(ChatDetails chat, Guid branchId, Guid serverId, string name, string schemaHash);
    IReadOnlyList<ToolPolicySettings> BranchToolPolicies(ChatDetails chat, Guid branchId, Guid serverId, string name, string schemaHash);
}

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
