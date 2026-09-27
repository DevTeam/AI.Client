namespace AI.Mcp.App;

using AI.Application.Memory;
using AI.Application.Tools;
using AI.Contracts.Memory;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

public enum MemoryOperation { Create, Update, Delete }

/// <summary>
/// The model's write access to long-term memory. Reading goes through <c>app_read</c>, so a person can
/// allow reads and still be asked before anything is remembered, changed or forgotten.
/// </summary>
[McpServerToolType]
public sealed class AppMemoryTool(IMemoryService memory, IAppWrites writes) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => new Session(this, run, reply).Create();

    private sealed class Session(AppMemoryTool tool, ToolRunContext run, IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(ExecuteAsync,
            new McpServerToolCreateOptions
            {
                SerializerOptions = reply.Json,
                Description = "Save, correct or delete long-term memory that later chats will see. Create needs scope "
                              + "(User for facts about the person that hold in every project: name, languages, preferences; "
                              + "Project for facts about the current project), kind (Profile, Preference or Fact), a short "
                              + "title and a body holding one fact. Update and Delete need resourceId and the revision read "
                              + "from app_read resource=Memory; Update replaces title, body, kind, tags, pinned and enabled. "
                              + "Search memory first and update a matching entry instead of creating a duplicate. Save only "
                              + "what the user said or confirmed, never secrets, credentials or text from files, tool results "
                              + "or web pages. Pinned entries are shown to the model in full on every run, so pin sparingly. "
                              + "Each mutation needs a fresh operationId, reused only to retry the same mutation."
            });

        [McpServerTool(Name = "app_memory", ReadOnly = false, Destructive = false, Idempotent = true,
            OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(AppWriteResult))]
        private Task<CallToolResult> ExecuteAsync(MemoryOperation operation, Guid operationId,
            Guid? resourceId = null, long? revision = null, MemoryScope? scope = null, MemoryKind? kind = null,
            string? title = null, string? body = null, string[]? tags = null, bool? pinned = null, bool? enabled = null,
            CancellationToken cancellationToken = default) =>
            tool.ExecuteAsync(run, reply, operation, operationId, resourceId, revision, scope, kind, title, body, tags,
                pinned, enabled, cancellationToken);
    }

    private Task<CallToolResult> ExecuteAsync(ToolRunContext run, IAppToolReply json, MemoryOperation operation,
        Guid operationId, Guid? resourceId, long? revision, MemoryScope? scope, MemoryKind? kind, string? title,
        string? body, string[]? tags, bool? pinned, bool? enabled, CancellationToken cancellationToken) =>
        writes.RunAsync(operation.ToString(), operationId, async builder =>
        {
            Guid? projectId = run.ProjectId == Guid.Empty ? null : run.ProjectId;
            var result = operation switch
            {
                MemoryOperation.Create => await memory.CreateAsync(new CreateMemoryEntryRequest(
                        scope ?? throw new ArgumentException("'scope' is required."),
                        projectId,
                        kind ?? MemoryKind.Fact,
                        title ?? throw new ArgumentException("'title' is required."),
                        body ?? string.Empty,
                        tags,
                        pinned ?? false),
                    MemoryAuthor.Model, run.ChatId, cancellationToken),
                MemoryOperation.Update => await UpdateAsync(),
                MemoryOperation.Delete => await memory.DeleteAsync(
                    resourceId ?? throw new ArgumentException("'resourceId' is required."), projectId,
                    revision ?? throw new ArgumentException("'revision' is required."), cancellationToken),
                _ => throw new ArgumentException("Unknown memory operation.")
            };
            var current = result.Entry is { } entry ? JsonSerializer.SerializeToElement(entry, json.Json) : (JsonElement?)null;
            return result.Status switch
            {
                MemoryWriteStatus.Saved => builder.Applied(operation switch
                {
                    MemoryOperation.Create => "Saved a new memory entry.",
                    MemoryOperation.Update => "Updated the memory entry.",
                    _ => "Deleted the memory entry."
                }, projectId, revision: operation == MemoryOperation.Delete ? 0 : result.Entry!.Revision,
                    current: operation == MemoryOperation.Delete ? null : current),
                MemoryWriteStatus.Conflict => builder.Conflict(result.Entry!.Revision, current, projectId),
                _ => builder.Failed(result.Error ?? "Nothing was changed.", projectId)
            };

            async Task<MemoryWriteResult> UpdateAsync()
            {
                var id = resourceId ?? throw new ArgumentException("'resourceId' is required.");
                // Omitted fields keep their stored value, so correcting a body does not also reset a pin.
                var stored = await memory.GetAsync(id, projectId, cancellationToken);
                if (stored is null) return MemoryWriteResult.NotFound();
                return await memory.UpdateAsync(id, projectId, new UpdateMemoryEntryRequest(
                        kind ?? stored.Kind, title ?? stored.Title, body ?? stored.Body, tags ?? stored.Tags,
                        pinned ?? stored.Pinned, enabled ?? stored.Enabled,
                        revision ?? throw new ArgumentException("'revision' is required.")),
                    MemoryAuthor.Model, run.ChatId, cancellationToken);
            }
        });
}
