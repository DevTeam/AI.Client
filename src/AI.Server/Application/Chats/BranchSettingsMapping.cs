namespace AI.Application.Chats;

using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Settings;
using AI.Domain.Chats;
using AI.Domain.Projects;

/// <summary>Maps a branch's optional overrides at the application boundary.</summary>
internal static class BranchSettingsMapping
{
    public static ChatBranchSettings? ToDomain(BranchSettings? settings)
    {
        if (settings is null) return null;
        var policies = settings.ToolPolicies is { Count: > 0 } items ? items.Select(policy => new ToolPolicy(
            new ToolIdentity(new McpServerId(policy.ServerId), policy.Name, policy.SchemaHash),
            Enum.TryParse<ToolPolicyDecision>(policy.Decision, true, out var decision) && Enum.IsDefined(decision)
                ? decision : throw new ArgumentException($"Unsupported tool policy '{policy.Decision}'."),
            policy.MaxCallsPerRun,
            policy.TimeoutSeconds is { } timeout ? TimeSpan.FromSeconds(timeout) : null)).ToArray() : null;
        if (settings.ReasoningEffort is { } effort && !ReasoningEffortValues.IsSupported(effort))
            throw new ArgumentException("Unsupported reasoning effort.");
        return settings.ConnectionId is null && settings.ApprovalMode is null && policies is null
            && settings.ReasoningEffort is null ? null
            : new ChatBranchSettings(settings.ConnectionId is { } connection ? new ConnectionId(connection) : null,
                settings.ApprovalMode is { } mode ? mode switch
                {
                    ToolApprovalMode.Ask => ChatApprovalMode.Ask,
                    ToolApprovalMode.Auto => ChatApprovalMode.Auto,
                    ToolApprovalMode.FullAccess => ChatApprovalMode.FullAccess,
                    _ => throw new ArgumentException("Unknown branch approval mode.")
                } : null, policies, settings.ReasoningEffort);
    }

    public static BranchSettings? ToContract(ChatBranchSettings? settings) => settings is null ? null : new(
        settings.ConnectionId?.Value,
        settings.ApprovalMode is { } mode ? mode switch
        {
            ChatApprovalMode.Auto => ToolApprovalMode.Auto,
            ChatApprovalMode.FullAccess => ToolApprovalMode.FullAccess,
            _ => ToolApprovalMode.Ask
        } : null,
        settings.ToolPolicies?.Select(policy => new ToolPolicySettings(policy.Tool.ServerId.Value,
            policy.Tool.Name, policy.Tool.SchemaHash, policy.Decision.ToString(), policy.MaxCallsPerRun,
            policy.Timeout is { } timeout ? checked((long)timeout.TotalSeconds) : null)).ToArray(),
        settings.ReasoningEffort);
}
