namespace AI.Mcp.App;

using AI.Application.Chat;
using AI.Application.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

public enum ContextCompactionAction
{
    Preview,
    Compact,
    Reset
}

public sealed record ContextCompactionToolResult(
    string Action,
    int CoveredMessages,
    long SourceCharacters,
    int SummaryCharacters,
    bool Applied,
    string Guidance,
    string? Error = null);

[McpServerToolType]
public sealed class AppContextCompactTool(IModelContentCheckpointService checkpoints) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => new Session(checkpoints, run, reply).Create();

    private sealed class Session(IModelContentCheckpointService checkpoints, ToolRunContext run, IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(ExecuteAsync,
            new McpServerToolCreateOptions
            {
                SerializerOptions = reply.Json,
                Description = "Preview, create, or reset a model-only compaction checkpoint. Scope Turn summarizes completed work of "
                              + "the current turn for the rest of this run. Scope History summarizes the chat's earlier turns (all but the "
                              + "current and the previous one) and keeps the summary for every later request of this branch; use it when "
                              + "the conversation is long and older turns are no longer needed in detail. Compaction uses an isolated LLM "
                              + "request without tools. It never edits stored messages or the visible transcript."
            });

        [McpServerTool(Name = "context_compact", ReadOnly = false, Destructive = false, Idempotent = true,
            OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ContextCompactionToolResult))]
        private async Task<CallToolResult> ExecuteAsync(ContextCompactionAction action,
            ContextCompactionScope scope = ContextCompactionScope.Turn, int targetTokens = 1500,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (action == ContextCompactionAction.Preview)
                {
                    var preview = checkpoints.Preview(run, scope);
                    return reply.Reply(new ContextCompactionToolResult(action.ToString(), preview.CoveredMessages,
                        preview.SourceCharacters, 0, false, preview.CanCompact
                            ? scope == ContextCompactionScope.History
                                ? "Run Compact with scope History to replace these earlier turns in this and later requests."
                                : "Run Compact to replace this completed work in the next model request."
                            : scope == ContextCompactionScope.History
                                ? "Only the current and the previous turn are left; there is no earlier history to compact."
                                : "There is no completed work in this turn to compact."));
                }
                if (action == ContextCompactionAction.Reset)
                    return reply.Reply(new ContextCompactionToolResult(action.ToString(), 0, 0, 0,
                        await checkpoints.ResetAsync(run, scope, cancellationToken),
                        scope == ContextCompactionScope.History
                            ? "The next model request will use the full earlier history."
                            : "The next model request will use the uncompacted run context."));

                var result = await checkpoints.CompactAsync(run, targetTokens, scope, cancellationToken);
                return reply.Reply(new ContextCompactionToolResult(action.ToString(), result.CoveredMessages,
                    result.SourceCharacters, result.SummaryCharacters, result.Applied, result.Guidance), !result.Applied);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            {
                return reply.Reply(new ContextCompactionToolResult(action.ToString(), 0, 0, 0, false,
                    "Adjust the compaction request or continue without a checkpoint.", error.Message), true);
            }
        }
    }
}
