namespace AI.Mcp.App;

using AI.Application.Skills;
using AI.Application.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

public sealed record SkillSearchItem(string Id, string Name, string Description, string Source, string Kind,
    JsonElement? ParametersSchema, JsonElement? ResultSchema);
public sealed record SkillSearchResult(IReadOnlyList<SkillSearchItem> Skills, bool MatchedQuery, string Guidance);

[McpServerToolType]
public sealed class AppSkillSearchTool(ISkillGuide skills, ISkillCandidateSelector selector) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) =>
        new Session(skills, selector, run, reply).Create();

    private sealed class Session(ISkillGuide skills, ISkillCandidateSelector selector, ToolRunContext run, IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(Search,
            new McpServerToolCreateOptions
            {
                SerializerOptions = reply.Json,
                Description = "Find available skills by task or name. Omit query to browse enabled skills. "
                              + "If a query has no matches, retry with broader or alternate terms; "
                              + "MatchedQuery=false does not mean the catalog is empty. Broad results contain metadata only; "
                              + "search by exact skill ID to get its parameter and result schemas. A generic skill needs "
                              + "its data read with app_read and passed as parameters; a playbook returns instructions "
                              + "for you to follow with your own tools. Run either with mcp_app__run_skill."
            });

        [McpServerTool(Name = "skill_search", ReadOnly = true, Destructive = false, Idempotent = true,
            OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(SkillSearchResult))]
        private async Task<CallToolResult> Search(string? query = null, int limit = 30,
            CancellationToken cancellationToken = default)
        {
            var available = (await skills.EffectiveAsync(run.ProjectId, cancellationToken))
                .ToArray();
            var term = query?.Trim().TrimStart('/');
            var exact = available.FirstOrDefault(skill => skill.Id.Equals(term, StringComparison.OrdinalIgnoreCase));
            var selected = string.IsNullOrWhiteSpace(query)
                ? available.Take(Math.Clamp(limit, 1, 50)).ToArray()
                : exact is not null ? [exact] : selector.FindCandidates(query, available, Math.Clamp(limit, 1, 50));
            var matchedQuery = available.Length > 0 && (string.IsNullOrWhiteSpace(query) || selected.Count > 0);
            var matches = selected
                .Select(item => new SkillSearchItem(item.Id, item.Name, exact is null ? Brief(item.Description) : item.Description,
                    item.Source, item.Kind, exact is not null ? item.ParametersSchema : null,
                    exact is not null ? item.ResultSchema : null))
                .ToArray();
            var guidance = available.Length == 0
                ? "No enabled skills are available in this project."
                : string.IsNullOrWhiteSpace(query)
                    ? $"Showing {matches.Length} of {available.Length} enabled skills. Search by task or exact ID for others."
                : matchedQuery
                    ? "These enabled skills match the query. Search by exact ID for full schemas."
                    : "No skill matched the query. Retry with broader or alternate terms, or omit the query to browse enabled skills.";
            return reply.Reply(new SkillSearchResult(matches, matchedQuery, guidance));
        }

        private static string Brief(string description)
        {
            var flat = description.ReplaceLineEndings(" ").Trim();
            return flat.Length <= 200 ? flat : flat[..200] + "...";
        }
    }
}
