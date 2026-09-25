namespace AI.Client.Mcp.App;

using AI.Client.Application.Instructions;
using AI.Client.Application.Tools;
using AI.Client.Contracts.Instructions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

/// <summary>
/// Proposes a new version of the project's instructions. It is a tool of its own, apart from memory
/// and project settings, so that it keeps asking for approval even where those are allowed: rules
/// the model could rewrite without a person looking would outlive the chat that wrote them.
/// </summary>
[McpServerToolType]
public sealed class AppInstructionsTool(IProjectInstructionsService instructions, IAppWrites writes) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => new Session(this, run, reply).Create();

    private sealed class Session(AppInstructionsTool tool, ToolRunContext run, IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(ExecuteAsync,
            new McpServerToolCreateOptions
            {
                SerializerOptions = reply.Json,
                Description = "Replace the current project's instructions: the rules the user wrote for how you work in this "
                              + "project, sent in full on every run. Use it only when the user asks to change those rules. Read "
                              + "app_read resource=Instructions first, send the complete new text rather than a fragment, and "
                              + "pass the revision you read. includeWorkspaceFiles controls whether AGENTS.md and CLAUDE.md at the "
                              + "project's directory roots are added; omit it to keep the current setting. The user reviews "
                              + "the change before it applies. Facts and preferences belong in app_memory, not here. "
                              + "Each change needs a fresh operationId."
            });

        [McpServerTool(Name = "app_instructions", ReadOnly = false, Destructive = true, Idempotent = true,
            OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(AppWriteResult))]
        private Task<CallToolResult> ExecuteAsync(Guid operationId, string text, long revision,
            bool? includeWorkspaceFiles = null, CancellationToken cancellationToken = default) =>
            tool.ExecuteAsync(run, reply, operationId, text, revision, includeWorkspaceFiles, cancellationToken);
    }

    private Task<CallToolResult> ExecuteAsync(ToolRunContext run, IAppToolReply json, Guid operationId, string text,
        long revision, bool? includeWorkspaceFiles, CancellationToken cancellationToken) =>
        writes.RunAsync("UpdateInstructions", operationId, async builder =>
        {
            if (run.ProjectId == Guid.Empty) return builder.Failed("This run has no project.");
            var stored = await instructions.GetAsync(run.ProjectId, cancellationToken);
            if (stored is null) return builder.Failed("Project not found.", run.ProjectId);
            var result = await instructions.UpdateAsync(run.ProjectId, new UpdateProjectInstructionsRequest(
                text, includeWorkspaceFiles ?? stored.IncludeWorkspaceFiles, revision), cancellationToken);
            var current = result.Instructions is { } document
                ? JsonSerializer.SerializeToElement(document, json.Json) : (JsonElement?)null;
            return result.Status switch
            {
                ProjectInstructionsUpdateStatus.Updated => builder.Applied(
                    "Updated the project instructions; they apply from the next run.", run.ProjectId,
                    revision: result.Instructions!.Revision, current: current),
                ProjectInstructionsUpdateStatus.Conflict => builder.Conflict(result.Instructions!.Revision, current, run.ProjectId),
                _ => builder.Failed(result.Error ?? "Nothing was changed.", run.ProjectId)
            };
        });
}
