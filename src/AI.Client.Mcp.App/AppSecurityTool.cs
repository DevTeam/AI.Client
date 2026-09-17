namespace AI.Client.Mcp.App;

using AI.Client.Application.Chats;
using AI.Client.Application.Projects;
using AI.Client.Application.Settings;
using AI.Client.Application.Tools;
using AI.Client.Contracts.Projects;
using AI.Client.Contracts.Settings;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

public enum SecurityOperation
{
    /// <summary>Replace a project's whole access state at once. Needs 'projectId', 'revision' and 'security'.</summary>
    SetProjectSecurity,

    /// <summary>Set one tool's policy for a project. Needs 'projectId' and 'toolPolicy'.</summary>
    SetProjectToolPolicy,

    /// <summary>Drop one tool's project policy. Needs 'projectId', 'serverId', 'name' and 'schemaHash'.</summary>
    RemoveProjectToolPolicy,

    /// <summary>Set one tool's policy for a single chat. Needs 'projectId', 'chatId' and 'toolPolicy'.</summary>
    SetChatToolPolicy,

    /// <summary>Drop one tool's chat policy. Needs 'projectId', 'chatId', 'serverId', 'name' and 'schemaHash'.</summary>
    RemoveChatToolPolicy,

    /// <summary>Replace global connections and MCP servers at once. Needs 'settings'.</summary>
    SaveGlobalSettings,

    /// <summary>Set one tool's global policy. Needs 'toolPolicy'.</summary>
    SetGlobalToolPolicy,

    /// <summary>Drop one tool's global policy. Needs 'serverId', 'name' and 'schemaHash'.</summary>
    RemoveGlobalToolPolicy,

    /// <summary>Store a connection's API key. Needs 'serverId' as the connection id and 'secret'.</summary>
    SetConnectionCredential,

    /// <summary>Store an MCP server's credential. Needs 'serverId' and 'secret'.</summary>
    SetMcpCredential,
}

[McpServerToolType]
public sealed class AppSecurityTool(
    IProjectService projects,
    IChatService chats,
    IGlobalSettingsService settings,
    IAppWrites writes) : IAppTool
{
    public McpServerTool Create(ToolRunContext run) => McpServerTool.Create(
        SecurityAsync,
        new McpServerToolCreateOptions
        {
            SerializerOptions = ToolReply.Json,
            Description = "Change this application's access settings: directory grants, MCP server bindings, per-tool policies and "
                          + "connection credentials. 'SetProjectSecurity' and 'SaveGlobalSettings' replace the whole state they cover, so "
                          + "read it with 'app_read' first and send it back with your change applied — anything you leave out is removed. "
                          + "Secrets are write-only: a key can be stored and never read back, and passing null clears it. 'operationId' "
                          + "must be a fresh UUID per distinct change. You may grant a directory to your own project with "
                          + "'SetProjectSecurity' rather than asking the user to do it in the settings — but a grant reaches the file "
                          + "system tools only when their session next opens, so it takes effect from the following run and not from this "
                          + "one. Say that you have added it and what it will allow, instead of reporting that you have no access."
        });

    [McpServerTool(Name = "app_security", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(AppWriteResult))]
    private Task<CallToolResult> SecurityAsync(
        SecurityOperation operation,
        Guid operationId,
        Guid? projectId = null,
        Guid? chatId = null,
        long revision = 0,
        ProjectSecurityPayload? security = null,
        ToolPolicyPayload? toolPolicy = null,
        GlobalSettingsPayload? settings = null,
        Guid? serverId = null,
        string? name = null,
        string? schemaHash = null,
        string? secret = null,
        CancellationToken cancellationToken = default) =>
        writes.RunAsync(operation.ToString(), operationId, builder => operation switch
        {
            SecurityOperation.SetProjectSecurity => SetProjectSecurityAsync(builder, projectId, revision, security, cancellationToken),
            SecurityOperation.SetProjectToolPolicy => SetProjectToolPolicyAsync(builder, projectId, toolPolicy, cancellationToken),
            SecurityOperation.RemoveProjectToolPolicy => RemoveProjectToolPolicyAsync(builder, projectId, serverId, name, schemaHash, cancellationToken),
            SecurityOperation.SetChatToolPolicy => SetChatToolPolicyAsync(builder, projectId, chatId, toolPolicy, cancellationToken),
            SecurityOperation.RemoveChatToolPolicy => RemoveChatToolPolicyAsync(builder, projectId, chatId, serverId, name, schemaHash, cancellationToken),
            SecurityOperation.SaveGlobalSettings => SaveGlobalSettingsAsync(builder, settings, cancellationToken),
            SecurityOperation.SetGlobalToolPolicy => SetGlobalToolPolicyAsync(builder, toolPolicy, cancellationToken),
            SecurityOperation.RemoveGlobalToolPolicy => RemoveGlobalToolPolicyAsync(builder, serverId, name, schemaHash, cancellationToken),
            SecurityOperation.SetConnectionCredential => SetCredentialAsync(builder, serverId, secret, true, cancellationToken),
            SecurityOperation.SetMcpCredential => SetCredentialAsync(builder, serverId, secret, false, cancellationToken),
            _ => throw new ArgumentException("Unknown operation.", nameof(operation)),
        });

    private async Task<AppWriteResult> SetProjectSecurityAsync(
        AppWriteBuilder builder, Guid? projectId, long revision, ProjectSecurityPayload? security, CancellationToken cancellationToken)
    {
        var id = Required(projectId, nameof(projectId));
        var payload = security ?? throw new ArgumentException("'security' is required for this operation.", nameof(security));
        var current = await projects.GetAsync(id, cancellationToken) ?? throw new InvalidOperationException("Project not found.");
        var result = await projects.UpdateSecurityAsync(id, new UpdateProjectSecurityRequest(
            revision,
            payload.DirectoryGrants.Select(grant => new DirectoryGrantSettings(
                grant.Id, grant.DisplayName, grant.CanonicalRoot, grant.Recursive, grant.ToolNames)).ToArray(),
            payload.McpServers.Select(server => new AI.Client.Contracts.Projects.McpServerSettings(
                server.Id, server.DisplayName, server.Transport, server.Enabled)).ToArray(),
            payload.ToolPolicies.Select(Policy).ToArray()), cancellationToken);
        return AppProjectsTool.Describe(builder, result, id,
            project => $"Replaced the access settings of '{project.Name}': {project.DirectoryGrants.Count} grant(s), "
                       + $"{project.McpServers.Count} server binding(s), {project.ToolPolicies.Count} tool policy(ies).", current);
    }

    private async Task<AppWriteResult> SetProjectToolPolicyAsync(
        AppWriteBuilder builder, Guid? projectId, ToolPolicyPayload? toolPolicy, CancellationToken cancellationToken)
    {
        var id = Required(projectId, nameof(projectId));
        var policy = Policy(Required(toolPolicy, nameof(toolPolicy)));
        var project = await projects.SetToolPolicyAsync(id, policy, cancellationToken);
        return project is null
            ? builder.Failed("Project not found.", id)
            : builder.Applied($"Set '{policy.Name}' to {policy.Decision} for this project.", id,
                revision: project.Revision, current: Element(project));
    }

    private async Task<AppWriteResult> RemoveProjectToolPolicyAsync(
        AppWriteBuilder builder, Guid? projectId, Guid? serverId, string? name, string? schemaHash, CancellationToken cancellationToken)
    {
        var id = Required(projectId, nameof(projectId));
        var project = await projects.RemoveToolPolicyAsync(id, Required(serverId, nameof(serverId)),
            Text(name, nameof(name)), Text(schemaHash, nameof(schemaHash)), cancellationToken);
        return project is null
            ? builder.Failed("Project not found.", id)
            : builder.Applied($"Removed the project policy for '{name}'.", id, revision: project.Revision, current: Element(project));
    }

    private async Task<AppWriteResult> SetChatToolPolicyAsync(
        AppWriteBuilder builder, Guid? projectId, Guid? chatId, ToolPolicyPayload? toolPolicy, CancellationToken cancellationToken)
    {
        var project = Required(projectId, nameof(projectId));
        var chat = Required(chatId, nameof(chatId));
        var policy = Policy(Required(toolPolicy, nameof(toolPolicy)));
        var updated = await chats.SetToolPolicyAsync(project, chat, policy, cancellationToken);
        return updated is null
            ? builder.Failed("Chat not found.", project, chat)
            : builder.Applied($"Set '{policy.Name}' to {policy.Decision} for this chat.", project, chat,
                revision: updated.Revision, current: Element(updated with { Messages = [] }));
    }

    private async Task<AppWriteResult> RemoveChatToolPolicyAsync(
        AppWriteBuilder builder, Guid? projectId, Guid? chatId, Guid? serverId, string? name, string? schemaHash,
        CancellationToken cancellationToken)
    {
        var project = Required(projectId, nameof(projectId));
        var chat = Required(chatId, nameof(chatId));
        var updated = await chats.RemoveToolPolicyAsync(project, chat, Required(serverId, nameof(serverId)),
            Text(name, nameof(name)), Text(schemaHash, nameof(schemaHash)), cancellationToken);
        return updated is null
            ? builder.Failed("Chat not found.", project, chat)
            : builder.Applied($"Removed the chat policy for '{name}'.", project, chat,
                revision: updated.Revision, current: Element(updated with { Messages = [] }));
    }

    private async Task<AppWriteResult> SaveGlobalSettingsAsync(
        AppWriteBuilder builder, GlobalSettingsPayload? payload, CancellationToken cancellationToken)
    {
        var value = payload ?? throw new ArgumentException("'settings' is required for this operation.", nameof(payload));
        var stored = await settings.GetAsync(cancellationToken);
        var saved = await settings.SaveAsync(new SaveGlobalSettingsRequest(
            value.Connections.Select(connection => new ConnectionSettings(
                connection.Id, connection.Name, connection.BaseUrl, connection.Model, connection.Enabled, connection.IsDefault,
                // Credentials live in their own store and are never part of this document; the
                // flag is recomputed from what is actually held.
                stored.Connections.Any(item => item.Id == connection.Id && item.HasCredential),
                connection.ForSubtasks, connection.Capability, connection.Cost, connection.GoodFor)).ToArray(),
            value.McpServers.Select(server => new AI.Client.Contracts.Settings.McpServerSettings(
                server.Id, server.Name, server.Transport, server.Enabled, server.Policy, server.Url, server.Command,
                server.Arguments, server.WorkingDirectory,
                server.EnvironmentVariables.Select(variable => new McpEnvironmentVariableSettings(
                    variable.Name, variable.Value, variable.IsSecret, false)).ToArray(),
                stored.McpServers.Any(item => item.Id == server.Id && item.HasCredential))).ToArray(),
            value.ToolPolicies.Select(GlobalPolicy).ToArray()), cancellationToken);
        return builder.Applied(
            $"Replaced global settings: {saved.Connections.Count} connection(s), {saved.McpServers.Count} MCP server(s), "
            + $"{saved.ToolPolicies.Count} tool policy(ies).", current: Element(saved));
    }

    private async Task<AppWriteResult> SetGlobalToolPolicyAsync(
        AppWriteBuilder builder, ToolPolicyPayload? toolPolicy, CancellationToken cancellationToken)
    {
        var policy = GlobalPolicy(Required(toolPolicy, nameof(toolPolicy)));
        var saved = await settings.SetToolPolicyAsync(policy, cancellationToken);
        return builder.Applied($"Set '{policy.Name}' to {policy.Decision} globally.", current: Element(saved));
    }

    private async Task<AppWriteResult> RemoveGlobalToolPolicyAsync(
        AppWriteBuilder builder, Guid? serverId, string? name, string? schemaHash, CancellationToken cancellationToken)
    {
        var saved = await settings.RemoveToolPolicyAsync(Required(serverId, nameof(serverId)),
            Text(name, nameof(name)), Text(schemaHash, nameof(schemaHash)), cancellationToken);
        return builder.Applied($"Removed the global policy for '{name}'.", current: Element(saved));
    }

    /// <summary>
    /// Writes a secret into the Host's protected store. The value never comes back out of any tool
    /// here, so the only thing reported is whether the target existed.
    /// </summary>
    private async Task<AppWriteResult> SetCredentialAsync(
        AppWriteBuilder builder, Guid? serverId, string? secret, bool connection, CancellationToken cancellationToken)
    {
        var id = Required(serverId, nameof(serverId));
        var stored = connection
            ? await settings.SetConnectionCredentialAsync(id, secret, cancellationToken)
            : await settings.SetMcpCredentialAsync(id, secret, cancellationToken);
        var subject = connection ? "connection" : "MCP server";
        return stored
            ? builder.Applied(secret is null ? $"Cleared the {subject}'s credential." : $"Stored a credential for the {subject}.")
            : builder.Failed($"No {subject} with that id.");
    }

    private static ToolPolicySettings Policy(ToolPolicyPayload payload) => new(
        payload.ServerId, payload.Name, payload.SchemaHash, payload.Decision, payload.MaxCallsPerRun, payload.TimeoutSeconds);

    private static McpToolPolicySettings GlobalPolicy(ToolPolicyPayload payload) => new(
        payload.ServerId, payload.Name, payload.SchemaHash, payload.Decision, payload.MaxCallsPerRun, payload.TimeoutSeconds);

    private static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, ToolReply.Json);

    private static Guid Required(Guid? value, string name) =>
        value ?? throw new ArgumentException($"'{name}' is required for this operation.", name);

    private static T Required<T>(T? value, string name) where T : class =>
        value ?? throw new ArgumentException($"'{name}' is required for this operation.", name);

    private static string Text(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"'{name}' is required for this operation.", name) : value;
}
