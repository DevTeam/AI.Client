namespace AI.Client.Mcp.App;

using AI.Client.Application.Resources;
using AI.Client.Application.Tools;
using AI.Client.Contracts.Resources;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

public enum ResourceOperation { Create, Retire }

/// <summary>Manages project references; actual attachment is atomic with app_runs Submit.</summary>
[McpServerToolType]
public sealed class AppResourcesTool(IResourceService resources, IAppWrites writes, IAppToolReply reply) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => McpServerTool.Create(ExecuteAsync,
        new McpServerToolCreateOptions
        {
            SerializerOptions = reply.Json,
            Description = "Create or retire project file and directory references. Create validates a path against "
                          + "the project's read grants and returns a reusable reference without reading file contents. "
                          + "Use app_read Resources to list references, and app_runs Submit to attach one to a chat turn. "
                          + "Retire needs the resource id and revision; earlier submitted turns keep their reference. "
                          + "Each mutation needs a fresh operationId, reused only to retry the same mutation."
        });

    [McpServerTool(Name = "app_resources", ReadOnly = false, Destructive = false, Idempotent = true,
        OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(AppWriteResult))]
    private Task<CallToolResult> ExecuteAsync(ResourceOperation operation, Guid projectId, Guid operationId,
        ChatResourceKind? kind = null, string? path = null,
        Guid? resourceId = null, long? revision = null, CancellationToken cancellationToken = default)
        => writes.RunAsync(operation.ToString(), operationId, async builder =>
        {
            ResourceDefinition? result = operation switch
            {
                ResourceOperation.Create => new ResourceDefinition(await resources.CreateAsync(projectId,
                    kind ?? throw new ArgumentException("'kind' is required."),
                    path ?? throw new ArgumentException("'path' is required."), cancellationToken), 1, false),
                ResourceOperation.Retire => await resources.RetireAsync(projectId,
                    resourceId ?? throw new ArgumentException("'resourceId' is required."),
                    revision ?? throw new ArgumentException("'revision' is required."), cancellationToken),
                _ => throw new ArgumentException("Unknown resource operation.")
            };
            return result is null ? builder.Failed("Resource not found.", projectId)
                : builder.Applied(operation == ResourceOperation.Create ? "Created or reused a project reference."
                    : "Retired the project reference.", projectId, revision: result.Revision,
                    current: JsonSerializer.SerializeToElement(result, reply.Json));
        });
}
