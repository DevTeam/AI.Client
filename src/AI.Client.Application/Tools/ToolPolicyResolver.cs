namespace AI.Client.Application.Tools;

using Chats;
using Projects;
using Settings;

/// <summary>What the layered policies add up to for one tool in one chat.</summary>
public sealed record EffectiveToolPolicy(string Decision, int MaxCalls, long TimeoutSeconds);

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
public sealed class ToolPolicyResolver(IProjectService projects, IChatService chats, IGlobalSettingsRepository settings)
{
    public async Task<EffectiveToolPolicy> ResolveAsync(
        Guid projectId, Guid chatId, Guid serverId, string name, string schemaHash, CancellationToken cancellationToken)
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
        var chatPolicy = chat.ToolPolicies?.SingleOrDefault(item => item.ServerId == serverId
            && item.Name == name && item.SchemaHash == schemaHash);
        var globalPolicy = global.ToolPolicies.SingleOrDefault(item => item.ServerId == serverId
            && item.Name == name && item.SchemaHash == schemaHash);
        var policyDecision = chatPolicy?.Decision ?? projectPolicy?.Decision ?? globalPolicy?.Decision ?? "Ask";
        var decision = server is not { Enabled: true } || server.Policy == "Deny" || binding is { Enabled: false } || policyDecision == "Deny"
            ? "Deny" : policyDecision == "Allow" ? "Allow" : "Ask";
        return new EffectiveToolPolicy(
            decision,
            Math.Clamp(chatPolicy?.MaxCallsPerRun ?? projectPolicy?.MaxCallsPerRun ?? globalPolicy?.MaxCallsPerRun ?? 65535, 1, int.MaxValue),
            Math.Clamp(chatPolicy?.TimeoutSeconds ?? projectPolicy?.TimeoutSeconds ?? globalPolicy?.TimeoutSeconds ?? 120, 1, 600));
    }
}
