namespace AI.Client.Mcp.App;

using AI.Client.Application.Resources;
using AI.Client.Application.Tools;
using AI.Client.Contracts.Resources;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

public enum ResourceOperation { Create, Retire, CreateReview, UpdateReview }

/// <summary>Manages project references; actual attachment is atomic with app_runs Submit.</summary>
[McpServerToolType]
public sealed class AppResourcesTool(IResourceService resources, IReviewService reviews, IAppWrites writes, IAppToolReply reply) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => McpServerTool.Create(ExecuteAsync,
        new McpServerToolCreateOptions
        {
            SerializerOptions = reply.Json,
            Description = "Create or retire project file and directory references; create or update mutable chat reviews. "
                          + "A diff review names one saved assistant change set; a message review comments on selected text. "
                          + "Create validates a path against "
                          + "the project's read grants and returns a reusable reference without reading file contents. "
                          + "Use app_read Resources to list references, and app_runs Submit to attach one to a chat turn. "
                          + "Retire needs the resource id and revision; earlier submitted turns keep their reference. "
                          + "Each mutation needs a fresh operationId, reused only to retry the same mutation."
        });

    [McpServerTool(Name = "app_resources", ReadOnly = false, Destructive = false, Idempotent = true,
        OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(AppWriteResult))]
    private Task<CallToolResult> ExecuteAsync(ResourceOperation operation, Guid projectId, Guid operationId,
        ChatResourceKind? kind = null, string? path = null,
        Guid? resourceId = null, long? revision = null, Guid? chatId = null,
        Guid? sourceMessageId = null, string? name = null, string[]? files = null,
        ReviewComment[]? comments = null, ChatReviewKind? reviewKind = null,
        MessageReviewComment[]? messageComments = null, CancellationToken cancellationToken = default)
        => writes.RunAsync(operation.ToString(), operationId, async builder =>
        {
            if (operation == ResourceOperation.CreateReview)
            {
                var selectedReviewKind = reviewKind ?? ChatReviewKind.Diff;
                if (selectedReviewKind == ChatReviewKind.Diff && comments is not { Length: > 0 })
                    throw new ArgumentException("A diff review needs at least one comment.");
                var review = await reviews.CreateAsync(projectId,
                    chatId ?? throw new ArgumentException("'chatId' is required."),
                    new CreateReviewRequest(sourceMessageId ?? throw new ArgumentException("'sourceMessageId' is required."),
                        name ?? throw new ArgumentException("'name' is required."), files ?? [],
                        selectedReviewKind, messageComments, comments), cancellationToken);
                return builder.Applied("Created review resource.", projectId, review.ChatId, revision: review.Revision,
                    current: JsonSerializer.SerializeToElement(review, reply.Json));
            }
            if (operation == ResourceOperation.UpdateReview)
            {
                var review = await reviews.UpdateAsync(projectId,
                    chatId ?? throw new ArgumentException("'chatId' is required."),
                    resourceId ?? throw new ArgumentException("'resourceId' is required."),
                    new UpdateReviewRequest(name ?? throw new ArgumentException("'name' is required."),
                        files ?? [], comments ?? [], revision ?? throw new ArgumentException("'revision' is required."),
                        messageComments),
                    cancellationToken);
                return review is null ? builder.Failed("Review not found.", projectId)
                    : builder.Applied("Updated review resource.", projectId, review.ChatId, revision: review.Revision,
                        current: JsonSerializer.SerializeToElement(review, reply.Json));
            }
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
