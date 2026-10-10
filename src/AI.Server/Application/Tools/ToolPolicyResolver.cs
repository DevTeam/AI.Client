namespace AI.Application.Tools;

using Chats;
using Projects;
using Settings;
using AI.Contracts.Settings;

/// <summary>What the layered policies add up to for one tool in one chat.</summary>
public sealed record EffectiveToolPolicy(
    string Decision,
    int MaxCalls,
    string MaxCallsScope,
    long TimeoutSeconds);

/// <summary>
/// Works out whether a tool may run, asks first, or is refused. Chat overrides project, project
/// overrides global, and the server's own switch overrides all three — a disabled or denied server
/// denies its tools whatever a narrower policy says.
/// </summary>
/// <remarks>
/// This lives apart from the agent because the answer is needed in two places that must not
/// disagree: before a call is made, and again while a confirmation prompt is waiting, in case the
/// user grants the tool from settings instead of from the prompt.
/// </remarks>
public sealed class ToolPolicyResolver(IProjectService projects, IChatService chats, IGlobalSettingsRepository settings,
    IToolDefaultDecision defaults, IChatBranchSettingsResolver branchSettings)
    : IToolPolicyResolver
{
    public async Task<EffectiveToolPolicy> ResolveAsync(
        Guid projectId, Guid chatId, Guid serverId, string name, string schemaHash, CancellationToken cancellationToken)
        => await ResolveAsync(projectId, chatId, chatId, serverId, name, schemaHash, cancellationToken);

    public async Task<EffectiveToolPolicy> ResolveAsync(Guid projectId, Guid chatId, Guid branchId,
        Guid serverId, string name, string schemaHash, CancellationToken cancellationToken)
    {
        var global = await settings.LoadAsync(cancellationToken);
        var server = global.McpServers.SingleOrDefault(item => item.Id == serverId);
        var project = await projects.GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Project not found.");
        var binding = project.McpServers.SingleOrDefault(item => item.Id == serverId);
        var projectPolicy = project.ToolPolicies.SingleOrDefault(item => item.ServerId == serverId
            && item.Name == name && item.SchemaHash == schemaHash);
        var chat = await chats.GetAsync(projectId, chatId, cancellationToken)
            ?? throw new InvalidOperationException("Chat not found.");
        var branchPolicies = branchSettings.BranchToolPolicies(chat, branchId, serverId, name, schemaHash);
        var chatPolicy = chat.ToolPolicies?.SingleOrDefault(item => item.ServerId == serverId
            && item.Name == name && item.SchemaHash == schemaHash);
        var globalPolicy = global.ToolPolicies.SingleOrDefault(item => item.ServerId == serverId
            && item.Name == name && item.SchemaHash == schemaHash);
        var policyDecision = (branchPolicies.Count > 0 ? branchPolicies[0].Decision : null)
            ?? chatPolicy?.Decision ?? projectPolicy?.Decision ?? globalPolicy?.Decision
            ?? defaults.GetDecision(serverId, name);
        var decision = server is not { Enabled: true } || server.Policy == "Deny" || binding is { Enabled: false } || policyDecision == "Deny"
            ? "Deny" : policyDecision == "Allow" ? "Allow" : "Ask";
        var maxCalls = branchPolicies.Select(policy => policy.MaxCallsPerRun).FirstOrDefault(value => value is not null)
            ?? chatPolicy?.MaxCallsPerRun ?? projectPolicy?.MaxCallsPerRun
            ?? globalPolicy?.MaxCallsPerRun ?? McpToolPolicySettings.DefaultMaxCallsPerRun;
        var maxCallsScope = branchPolicies.Any(policy => policy.MaxCallsPerRun is not null) ? "branch"
            : chatPolicy?.MaxCallsPerRun is not null ? "chat"
            : projectPolicy?.MaxCallsPerRun is not null ? "project"
            : globalPolicy?.MaxCallsPerRun is not null ? "global" : "default";
        var timeout = branchPolicies.Select(policy => policy.TimeoutSeconds).FirstOrDefault(value => value is not null)
            ?? chatPolicy?.TimeoutSeconds ?? projectPolicy?.TimeoutSeconds
            ?? globalPolicy?.TimeoutSeconds ?? McpToolPolicySettings.DefaultTimeoutSeconds;
        return new EffectiveToolPolicy(
            decision,
            Math.Clamp(maxCalls, 1, int.MaxValue),
            maxCallsScope,
            Math.Clamp(timeout, 1, McpToolPolicySettings.MaxTimeoutSeconds));
    }
}
