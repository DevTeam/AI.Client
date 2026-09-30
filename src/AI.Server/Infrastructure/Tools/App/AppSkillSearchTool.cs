namespace AI.Mcp.App;

using AI.Application.Skills;
using AI.Application.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

public sealed record SkillSearchItem(string Id, string Name, string Description, string Source, string Kind,
    JsonElement ParametersSchema, JsonElement? ResultSchema);
public sealed record SkillSearchResult(IReadOnlyList<SkillSearchItem> Skills, bool MatchedQuery, string Guidance);

[McpServerToolType]
public sealed class AppSkillSearchTool(ISkillGuide skills) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) =>
        new Session(skills, run, reply).Create();

    private sealed class Session(ISkillGuide skills, ToolRunContext run, IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(Search,
            new McpServerToolCreateOptions
            {
                SerializerOptions = reply.Json,
                Description = "Find available skills by task or name. Omit query to list all enabled skills. "
                              + "If a query has no matches, returns the available skills with MatchedQuery=false; "
                              + "do not infer that the catalog is empty. Returns each skill's ID, parameter schema "
                              + "result schema and kind. A generic skill needs its data read with app_read and passed as parameters; "
                              + "a playbook returns instructions for you to follow with your own tools. Run either with app_run_skill. Search before running a skill whose parameters you do not know."
            });

        [McpServerTool(Name = "skill_search", ReadOnly = true, Destructive = false, Idempotent = true,
            OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(SkillSearchResult))]
        private async Task<CallToolResult> Search(string? query = null, int limit = 30,
            CancellationToken cancellationToken = default)
        {
            var words = (query ?? string.Empty).Split([' ', '\t', '\r', '\n', '_', '-'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var available = (await skills.EffectiveAsync(run.ProjectId, cancellationToken))
                .Select(skill => new
                {
                    Skill = skill,
                    Score = words.Count(word => skill.Id.Contains(word, StringComparison.OrdinalIgnoreCase)
                        || skill.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
                        || skill.Aliases?.Any(alias => alias.Contains(word, StringComparison.OrdinalIgnoreCase)) == true
                        || skill.Description.Contains(word, StringComparison.OrdinalIgnoreCase))
                })
                .ToArray();
            var matchedQuery = available.Length > 0 && (words.Length == 0 || available.Any(item => item.Score > 0));
            var matches = available
                .Where(item => !matchedQuery || words.Length == 0 || item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Skill.Id, StringComparer.Ordinal)
                .Take(Math.Clamp(limit, 1, 50))
                .Select(item => new SkillSearchItem(item.Skill.Id, item.Skill.Name, item.Skill.Description,
                    item.Skill.Source, item.Skill.Kind, item.Skill.ParametersSchema, item.Skill.ResultSchema))
                .ToArray();
            var guidance = available.Length == 0
                ? "No enabled skills are available in this project."
                : matchedQuery
                    ? "These enabled skills match the query, or the query was omitted to list all skills."
                    : "No skill matched the query. These are the available enabled skills; choose one only if it fits the task.";
            return reply.Reply(new SkillSearchResult(matches, matchedQuery, guidance));
        }
    }
}
