namespace AI.Mcp.App;

using AI.Application.Skills;
using AI.Application.Tools;
using AI.Contracts.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

[McpServerToolType]
public sealed class AppSkillRunTool(ISkillRunner runner, IToolCatalogRegistry catalog) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => new Session(runner, catalog, run, reply).Create();

    private sealed class Session(ISkillRunner runner, IToolCatalogRegistry catalog, ToolRunContext run, IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(RunAsync,
            new McpServerToolCreateOptions
            {
                SerializerOptions = reply.Json,
                Description = "Run a skill found with app_skill_search. Pass its ID and a 'parameters' JSON object matching its "
                              + "schema; pass {} when every parameter is optional and none applies. The application supplies the current project scope; chat IDs outside it are rejected. "
                              + "A generic skill runs in an isolated model without tools: read its data with app_read first and pass it "
                              + "in the arguments; it returns JSON in output. A playbook returns output.instructions and output.context "
                              + "(current project, chat and branch ids): follow the instructions right away in this turn with your "
                              + "ordinary tools, then report the outcome to the user in one short line. "
                              + "Use chat-rename mode=requested only when the user explicitly asked to rename that chat. "
                              + "The result reports Completed, Skipped or Failed and includes the affected chat ID."
            });

        [McpServerTool(Name = "run_skill", ReadOnly = false, Destructive = true, Idempotent = false,
            OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(SkillRunRecord))]
        private async Task<CallToolResult> RunAsync(string skillId, JsonElement? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (run.ProjectId == Guid.Empty)
                return reply.Reply(new SkillRunRecord(Guid.Empty, skillId, run.ProjectId, null, "Failed",
                    "A project context is required to run a skill.", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), true);
            var result = await runner.RunAsync(new SkillInvocation(skillId, run.ProjectId, parameters ?? default, run.ChatId, run.BranchId), cancellationToken);
            // A playbook's steps need the tools it declares; pinning them here saves the model a
            // tool_search before it can take the first step. Permissions are unaffected.
            if (result.Output is { ValueKind: JsonValueKind.Object } output && output.TryGetProperty("kind", out var kind)
                && kind.GetString() == SkillKinds.Playbook && output.TryGetProperty("tools", out var tools))
                catalog.Pin(run, tools.EnumerateArray().Select(tool => tool.GetString() ?? string.Empty));
            return reply.Reply(result, result.Status is "Failed" or "Cancelled");
        }
    }
}
