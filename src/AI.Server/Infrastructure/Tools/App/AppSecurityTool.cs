namespace AI.Mcp.App;

using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Settings;
using AI.Application.Tools;
using AI.Contracts.Projects;
using AI.Contracts.Settings;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

public enum SecurityOperation
{
    /// <summary>Add one directory grant without changing other project security settings.</summary>
    AddDirectoryGrant,

    /// <summary>Remove one directory grant by 'grantId' without changing other project security settings.</summary>
    RemoveDirectoryGrant,

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

    /// <summary>Add or update one global connection. Needs 'connection'; updates also need 'expectedConnection'.</summary>
    UpsertConnection,

    /// <summary>Remove one global connection. Needs 'serverId' and 'expectedConnection'.</summary>
    RemoveConnection,

    /// <summary>Add or update one global MCP server. Needs 'mcpServer'; updates also need 'expectedMcpServer'.</summary>
    UpsertMcpServer,

    /// <summary>Remove one global MCP server. Needs 'serverId' and 'expectedMcpServer'.</summary>
    RemoveMcpServer,

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
    IAppWrites writes,
    IAppToolReply reply) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => McpServerTool.Create(
        SecurityAsync,
        new McpServerToolCreateOptions
        {
            SerializerOptions = reply.Json,
            Description = "Change this application's access settings: directory grants, MCP server bindings, per-tool policies and "
                          + "connection credentials. Use 'AddDirectoryGrant' with 'projectId', 'revision' and 'directoryGrant' "
                          + "to add one directory without changing server bindings or policies, and 'RemoveDirectoryGrant' with "
                          + "'projectId', 'revision' and 'grantId' (the grant's id from app_read) to revoke one. "
                          + "Use UpsertConnection or UpsertMcpServer to change one global item and RemoveConnection or "
                          + "RemoveMcpServer to delete one. Supply the previously read item as 'expectedConnection' or "
                          + "'expectedMcpServer' when changing an existing item. A new id needs no expected item. "
                          + "'SetProjectSecurity' and 'SaveGlobalSettings' replace the whole state they cover, so "
                          + "read it with 'app_read' first and send it back with your change applied — anything you leave out is removed. "
                          + "Secrets are write-only: a key can be stored and never read back, and passing null clears it. 'operationId' "
                          + "must be a fresh UUID per distinct change. Use AddDirectoryGrant to grant one directory in your project; "
                          + "the file tools refresh before the next model step. Calls already submitted in the same batch keep the old grants."
        });

    [McpServerTool(Name = "app_security", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(AppWriteResult))]
    private Task<CallToolResult> SecurityAsync(
        SecurityOperation operation,
        Guid operationId,
        Guid? projectId = null,
        Guid? chatId = null,
        long revision = 0,
        DirectoryGrantPayload? directoryGrant = null,
        Guid? grantId = null,
        ProjectSecurityPayload? security = null,
        ToolPolicyPayload? toolPolicy = null,
        GlobalSettingsPayload? settings = null,
        ConnectionPayload? connection = null,
        ConnectionPayload? expectedConnection = null,
        McpServerPayload? mcpServer = null,
        McpServerPayload? expectedMcpServer = null,
        Guid? serverId = null,
        string? name = null,
        string? schemaHash = null,
        string? secret = null,
        CancellationToken cancellationToken = default) =>
        writes.RunAsync(operation.ToString(), operationId, builder => operation switch
        {
            SecurityOperation.AddDirectoryGrant => AddDirectoryGrantAsync(builder, projectId, revision, directoryGrant, cancellationToken),
            SecurityOperation.RemoveDirectoryGrant => RemoveDirectoryGrantAsync(builder, projectId, revision, grantId, cancellationToken),
            SecurityOperation.SetProjectSecurity => SetProjectSecurityAsync(builder, projectId, revision, security, cancellationToken),
            SecurityOperation.SetProjectToolPolicy => SetProjectToolPolicyAsync(builder, projectId, toolPolicy, cancellationToken),
            SecurityOperation.RemoveProjectToolPolicy => RemoveProjectToolPolicyAsync(builder, projectId, serverId, name, schemaHash, cancellationToken),
            SecurityOperation.SetChatToolPolicy => SetChatToolPolicyAsync(builder, projectId, chatId, toolPolicy, cancellationToken),
            SecurityOperation.RemoveChatToolPolicy => RemoveChatToolPolicyAsync(builder, projectId, chatId, serverId, name, schemaHash, cancellationToken),
            SecurityOperation.SaveGlobalSettings => SaveGlobalSettingsAsync(builder, settings, cancellationToken),
            SecurityOperation.UpsertConnection => UpsertConnectionAsync(builder, connection, expectedConnection, cancellationToken),
            SecurityOperation.RemoveConnection => RemoveConnectionAsync(builder, serverId, expectedConnection, cancellationToken),
            SecurityOperation.UpsertMcpServer => UpsertMcpServerAsync(builder, mcpServer, expectedMcpServer, cancellationToken),
            SecurityOperation.RemoveMcpServer => RemoveMcpServerAsync(builder, serverId, expectedMcpServer, cancellationToken),
            SecurityOperation.SetGlobalToolPolicy => SetGlobalToolPolicyAsync(builder, toolPolicy, cancellationToken),
            SecurityOperation.RemoveGlobalToolPolicy => RemoveGlobalToolPolicyAsync(builder, serverId, name, schemaHash, cancellationToken),
            SecurityOperation.SetConnectionCredential => SetCredentialAsync(builder, serverId, secret, true, cancellationToken),
            SecurityOperation.SetMcpCredential => SetCredentialAsync(builder, serverId, secret, false, cancellationToken),
            _ => throw new ArgumentException("Unknown operation.", nameof(operation)),
        });

    private async Task<AppWriteResult> AddDirectoryGrantAsync(
        AppWriteBuilder builder, Guid? projectId, long revision, DirectoryGrantPayload? directoryGrant,
        CancellationToken cancellationToken)
    {
        var id = Required(projectId, nameof(projectId));
        var grant = Required(directoryGrant, nameof(directoryGrant));
        var current = await projects.GetAsync(id, cancellationToken);
        var result = await projects.AddDirectoryGrantAsync(id, revision, new DirectoryGrantSettings(
            grant.Id, grant.DisplayName, grant.CanonicalRoot, grant.Recursive, grant.ToolNames), cancellationToken);
        return AppProjectsTool.Describe(builder, result, id,
            project => $"Granted access to '{grant.CanonicalRoot}' in '{project.Name}'.", current, reply.Json);
    }

    private async Task<AppWriteResult> RemoveDirectoryGrantAsync(
        AppWriteBuilder builder, Guid? projectId, long revision, Guid? grantId, CancellationToken cancellationToken)
    {
        var id = Required(projectId, nameof(projectId));
        var grant = Required(grantId, nameof(grantId));
        var current = await projects.GetAsync(id, cancellationToken);
        var root = current?.DirectoryGrants.FirstOrDefault(item => item.Id == grant)?.CanonicalRoot
            ?? throw new ArgumentException("Directory grant not found.", nameof(grantId));
        var result = await projects.RemoveDirectoryGrantAsync(id, revision, grant, cancellationToken);
        return AppProjectsTool.Describe(builder, result, id,
            project => $"Revoked access to '{root}' in '{project.Name}'.", current, reply.Json);
    }

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
            payload.McpServers.Select(server => new AI.Contracts.Projects.McpServerSettings(
                server.Id, server.DisplayName, server.Transport, server.Enabled)).ToArray(),
            payload.ToolPolicies.Select(Policy).ToArray()), cancellationToken);
        return AppProjectsTool.Describe(builder, result, id,
            project => $"Replaced the access settings of '{project.Name}': {project.DirectoryGrants.Count} grant(s), "
                       + $"{project.McpServers.Count} server binding(s), {project.ToolPolicies.Count} tool policy(ies).", current, reply.Json);
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
                revision: project.Revision, current: Element(project, reply.Json));
    }

    private async Task<AppWriteResult> RemoveProjectToolPolicyAsync(
        AppWriteBuilder builder, Guid? projectId, Guid? serverId, string? name, string? schemaHash, CancellationToken cancellationToken)
    {
        var id = Required(projectId, nameof(projectId));
        var project = await projects.RemoveToolPolicyAsync(id, Required(serverId, nameof(serverId)),
            Text(name, nameof(name)), Text(schemaHash, nameof(schemaHash)), cancellationToken);
        return project is null
            ? builder.Failed("Project not found.", id)
            : builder.Applied($"Removed the project policy for '{name}'.", id, revision: project.Revision, current: Element(project, reply.Json));
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
                revision: updated.Revision, current: Element(updated with { Messages = [] }, reply.Json));
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
                revision: updated.Revision, current: Element(updated with { Messages = [] }, reply.Json));
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
                connection.ForSubtasks, connection.Capability, connection.GoodFor,
                connection.ContextWindowTokens, connection.ReservedOutputTokens,
                // Prices are kept when the payload leaves them out: a model rewriting the whole
                // document rarely restates them, and dropping them would silently unprice usage.
                connection.Prices ?? stored.Connections.SingleOrDefault(item => item.Id == connection.Id)?.Prices)).ToArray(),
            value.McpServers.Select(server => new AI.Contracts.Settings.McpServerSettings(
                server.Id, server.Name, server.Transport, server.Enabled, server.Policy, server.Url, server.Command,
                server.Arguments, server.WorkingDirectory,
                server.EnvironmentVariables.Select(variable => new McpEnvironmentVariableSettings(
                    variable.Name, variable.Value, variable.IsSecret, false)).ToArray(),
                stored.McpServers.Any(item => item.Id == server.Id && item.HasCredential))).ToArray(),
            value.ToolPolicies.Select(GlobalPolicy).ToArray()), cancellationToken);
        return builder.Applied(
            $"Replaced global settings: {saved.Connections.Count} connection(s), {saved.McpServers.Count} MCP server(s), "
            + $"{saved.ToolPolicies.Count} tool policy(ies).", current: Element(saved, reply.Json));
    }

    private async Task<AppWriteResult> UpsertConnectionAsync(AppWriteBuilder builder, ConnectionPayload? payload,
        ConnectionPayload? expected, CancellationToken cancellationToken)
    {
        var item = Connection(Required(payload, nameof(payload)));
        var saved = await settings.UpsertConnectionAsync(item, expected is null ? null : Connection(expected), cancellationToken);
        return builder.Applied($"Saved connection '{item.Name}'.", current: Element(saved, reply.Json));
    }

    private async Task<AppWriteResult> RemoveConnectionAsync(AppWriteBuilder builder, Guid? id,
        ConnectionPayload? expected, CancellationToken cancellationToken)
    {
        var saved = await settings.RemoveConnectionAsync(Required(id, nameof(id)),
            Connection(Required(expected, nameof(expected))), cancellationToken);
        return builder.Applied("Removed one connection.", current: Element(saved, reply.Json));
    }

    private async Task<AppWriteResult> UpsertMcpServerAsync(AppWriteBuilder builder, McpServerPayload? payload,
        McpServerPayload? expected, CancellationToken cancellationToken)
    {
        var item = McpServer(Required(payload, nameof(payload)));
        var saved = await settings.UpsertMcpServerAsync(item, expected is null ? null : McpServer(expected), cancellationToken);
        return builder.Applied($"Saved MCP server '{item.Name}'.", current: Element(saved, reply.Json));
    }

    private async Task<AppWriteResult> RemoveMcpServerAsync(AppWriteBuilder builder, Guid? id,
        McpServerPayload? expected, CancellationToken cancellationToken)
    {
        var saved = await settings.RemoveMcpServerAsync(Required(id, nameof(id)),
            McpServer(Required(expected, nameof(expected))), cancellationToken);
        return builder.Applied("Removed one MCP server and its global tool policies.", current: Element(saved, reply.Json));
    }

    private static ConnectionSettings Connection(ConnectionPayload item) => new(
        item.Id, item.Name, item.BaseUrl, item.Model, item.Enabled, item.IsDefault, false,
        item.ForSubtasks, item.Capability, item.GoodFor, item.ContextWindowTokens, item.ReservedOutputTokens, item.Prices);

    private static AI.Contracts.Settings.McpServerSettings McpServer(McpServerPayload item) => new(
        item.Id, item.Name, item.Transport, item.Enabled, item.Policy, item.Url, item.Command,
        item.Arguments, item.WorkingDirectory,
        item.EnvironmentVariables.Select(variable => new McpEnvironmentVariableSettings(
            variable.Name, variable.Value, variable.IsSecret, false)).ToArray(), false);

    private async Task<AppWriteResult> SetGlobalToolPolicyAsync(
        AppWriteBuilder builder, ToolPolicyPayload? toolPolicy, CancellationToken cancellationToken)
    {
        var policy = GlobalPolicy(Required(toolPolicy, nameof(toolPolicy)));
        var saved = await settings.SetToolPolicyAsync(policy, cancellationToken);
        return builder.Applied($"Set '{policy.Name}' to {policy.Decision} globally.", current: Element(saved, reply.Json));
    }

    private async Task<AppWriteResult> RemoveGlobalToolPolicyAsync(
        AppWriteBuilder builder, Guid? serverId, string? name, string? schemaHash, CancellationToken cancellationToken)
    {
        var saved = await settings.RemoveToolPolicyAsync(Required(serverId, nameof(serverId)),
            Text(name, nameof(name)), Text(schemaHash, nameof(schemaHash)), cancellationToken);
        return builder.Applied($"Removed the global policy for '{name}'.", current: Element(saved, reply.Json));
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

    private static JsonElement Element<T>(T value, JsonSerializerOptions options) => JsonSerializer.SerializeToElement(value, options);

    private static Guid Required(Guid? value, string name) =>
        value ?? throw new ArgumentException($"'{name}' is required for this operation.", name);

    private static T Required<T>(T? value, string name) where T : class =>
        value ?? throw new ArgumentException($"'{name}' is required for this operation.", name);

    private static string Text(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"'{name}' is required for this operation.", name) : value;
}
