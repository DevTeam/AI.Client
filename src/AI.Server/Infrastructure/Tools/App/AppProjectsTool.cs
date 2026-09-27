namespace AI.Mcp.App;

using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Runs;
using AI.Application.Tools;
using AI.Contracts.Projects;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

public enum ProjectOperation
{
    /// <summary>Create a project. Needs 'name'; 'revision' is ignored.</summary>
    Create,

    /// <summary>Change a project's name, description or connection. Needs 'projectId' and 'revision'.</summary>
    Update,

    /// <summary>Delete a project with every chat in it. Needs 'projectId' and 'revision'. Honours 'dryRun'.</summary>
    Delete,
}

[McpServerToolType]
public sealed class AppProjectsTool(IProjectService projects, IChatService chats, Func<IChatRunDispatcher> runs, IAppWrites writes, IAppToolReply reply) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => McpServerTool.Create(
        ProjectsAsync,
        new McpServerToolCreateOptions
        {
            SerializerOptions = reply.Json,
            Description = "Manage projects shown in this application. An application project is a top-level container for chats and project settings. "
                          + "When the user asks to create, rename, update or delete a project without explicitly mentioning a directory, repository, "
                          + "source-code project or issue tracker, use this tool; do not substitute filesystem tools such as 'create_directory'. "
                          + "Before Create, first call 'ask_user' with one 'directories' path question so the user selects one or more project directories. Do not create "
                          + "the project without at least one answered directory. After receiving the paths, derive a suggested project name: for one directory use its final "
                          + "segment; for several directories use the final segment of their nearest meaningful common parent, or join their distinct final segments with ' + ' "
                          + "when they have no meaningful common parent. If the user did not already provide a project name, call 'ask_user' again with a project-name question "
                          + "and an access question in the same call. Offer the derived name first as '<suggested name> (Recommended)', explain that it comes from the selected "
                          + "directories, and allow a custom name. When the suggested option is chosen, pass the name without the ' (Recommended)' suffix. Offer "
                          + "'Read/write (Recommended)' and 'Read only' for access. If the user already supplied a name, keep it and "
                          + "ask only about access after the directories are known. If the name question is dismissed, use the derived suggestion. Create then requires 'name' and "
                          + "does not require 'projectId', 'revision', 'app_read' or 'dryRun'. After Create, use the returned "
                          + "project id and revision with 'app_security' SetProjectSecurity. Add one recursive directory grant per selected path, using a fresh UUID and "
                          + "the path's final segment as its display name. For read/write use toolNames ['read','write','edit','delete']; for read-only use ['read']. "
                          + "Because the project is new, send empty mcpServers and toolPolicies. Directory access starts on the following run. Update requires 'projectId' "
                          + "and 'revision'; read the current project with 'app_read' first. Delete requires 'projectId' and 'revision', removes every "
                          + "chat in the project, and uses 'dryRun: true' by default; pass 'dryRun: false' only to apply the deletion. Directory grants, "
                          + "MCP server bindings and tool policies belong to 'app_security'. 'operationId' must be a fresh UUID per distinct change. "
                          + "Create and Update apply immediately and reject 'dryRun: true'."
        });

    [McpServerTool(Name = "app_projects", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(AppWriteResult))]
    private Task<CallToolResult> ProjectsAsync(
        ProjectOperation operation,
        Guid operationId,
        Guid? projectId = null,
        string? name = null,
        string? description = null,
        Guid? connectionId = null,
        bool useDefaultConnection = false,
        long revision = 0,
        bool? dryRun = null,
        CancellationToken cancellationToken = default) =>
        writes.RunAsync(operation.ToString(), operationId, builder => operation switch
        {
            _ when dryRun == true && operation != ProjectOperation.Delete =>
                throw new ArgumentException(
                    $"'{operation}' cannot be rehearsed; only Delete honours 'dryRun'.", nameof(dryRun)),
            ProjectOperation.Create => CreateAsync(builder, name, description, cancellationToken),
            ProjectOperation.Update => UpdateAsync(builder, projectId, name, description, connectionId, useDefaultConnection, revision, cancellationToken),
            ProjectOperation.Delete => DeleteAsync(builder, projectId, revision, dryRun ?? true, cancellationToken),
            _ => throw new ArgumentException("Unknown operation.", nameof(operation)),
        });

    private async Task<AppWriteResult> CreateAsync(
        AppWriteBuilder builder, string? name, string? description, CancellationToken cancellationToken)
    {
        var project = await projects.CreateAsync(
            new CreateProjectRequest(Text(name, nameof(name)), description ?? string.Empty), cancellationToken);
        return builder.Applied($"Created project '{project.Name}'.", project.Id, revision: project.Revision, current: Element(project, reply.Json));
    }

    private async Task<AppWriteResult> UpdateAsync(
        AppWriteBuilder builder, Guid? projectId, string? name, string? description, Guid? connectionId, bool useDefaultConnection, long revision,
        CancellationToken cancellationToken)
    {
        var id = Required(projectId, nameof(projectId));
        var stored = await projects.GetAsync(id, cancellationToken) ?? throw new InvalidOperationException("Project not found.");
        // Unspecified fields keep their stored values, so a caller changing one thing need not
        // re-send the rest and cannot blank it out by omission. 'useDefaultConnection' is the
        // explicit way to ask for the global default — 'connectionId' alone cannot distinguish
        // "leave it alone" from "switch back to default".
        var resolvedConnection = useDefaultConnection
            ? null
            : connectionId ?? stored.ConnectionId;
        var result = await projects.UpdateAsync(id, new UpdateProjectRequest(
            name ?? stored.Name, description ?? stored.Description, revision, resolvedConnection), cancellationToken);
        return Describe(builder, result, id, project => $"Updated project '{project.Name}'.", stored, reply.Json);
    }

    private async Task<AppWriteResult> DeleteAsync(
        AppWriteBuilder builder, Guid? projectId, long revision, bool dryRun, CancellationToken cancellationToken)
    {
        var id = Required(projectId, nameof(projectId));
        var project = await projects.GetAsync(id, cancellationToken) ?? throw new InvalidOperationException("Project not found.");
        if (dryRun)
        {
            var count = (await chats.ListAsync(id, cancellationToken)).Count;
            return builder.Planned($"Would delete project '{project.Name}' with {count} chat(s).",
                id, revision: project.Revision, current: Element(project, reply.Json));
        }

        var result = await runs().DeleteProjectAsync(id, revision, cancellationToken);
        return result.IsDeleted
            ? builder.Applied($"Deleted project '{project.Name}'.", id, revision: result.Revision)
            : builder.Conflict(result.Revision, Element(project, reply.Json), id);
    }

    internal static AppWriteResult Describe(
        AppWriteBuilder builder, ProjectUpdateResult result, Guid projectId, Func<ProjectDetails, string> effect,
        ProjectDetails? current, JsonSerializerOptions options) => result.Status switch
    {
        ProjectUpdateStatus.Updated when result.Project is { } project =>
            builder.Applied(effect(project), projectId, revision: project.Revision, current: Element(project, options)),
        ProjectUpdateStatus.Conflict =>
            builder.Conflict(result.Revision, current is null ? null : Element(current, options), projectId),
        _ => builder.Failed("Project not found.", projectId),
    };

    private static JsonElement Element<T>(T value, JsonSerializerOptions options) => JsonSerializer.SerializeToElement(value, options);

    private static Guid Required(Guid? value, string name) =>
        value ?? throw new ArgumentException($"'{name}' is required for this operation.", name);

    private static string Text(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"'{name}' is required for this operation.", name) : value;
}
