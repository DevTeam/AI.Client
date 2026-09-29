namespace AI.Mcp.App;

using AI.Application.Skills;
using AI.Application.Tools;
using AI.Contracts.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

[McpServerToolType]
public sealed class AppSkillRunTool(ISkillRunner runner) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => new Session(runner, run, reply).Create();

    private sealed class Session(ISkillRunner runner, ToolRunContext run, IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(RunAsync,
            new McpServerToolCreateOptions
            {
                SerializerOptions = reply.Json,
                Description = "Run a skill found with app_skill_search. Pass its ID and an arguments object matching its "
                              + "parameters schema. Prepare data with ordinary app_read calls first. The application supplies "
                              + "the current project scope; chat IDs outside it are rejected. "
                              + "Use chat-title mode=requested only when the user explicitly asked to rename that chat. "
                              + "The result reports Completed, Skipped or Failed and includes the affected chat ID."
            });

        [McpServerTool(Name = "run_skill", ReadOnly = false, Destructive = true, Idempotent = false,
            OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(SkillRunRecord))]
        private async Task<CallToolResult> RunAsync(string skillId, JsonElement parameters,
            CancellationToken cancellationToken = default)
        {
            if (run.ProjectId == Guid.Empty)
                return reply.Reply(new SkillRunRecord(Guid.Empty, skillId, run.ProjectId, null, "Failed",
                    "A project context is required to run a skill.", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), true);
            var result = await runner.RunAsync(new SkillInvocation(skillId, run.ProjectId, parameters, run.ChatId), cancellationToken);
            return reply.Reply(result, result.Status is "Failed" or "Cancelled");
        }
    }
}
