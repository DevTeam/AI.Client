namespace AI.Client.Mcp.App;

using AI.Client.Application.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

public sealed record ToolSearchResult(IReadOnlyList<ToolCatalogMatch> Tools, string Guidance, string? Error = null);

[McpServerToolType]
public sealed class AppToolSearchTool(IToolCatalogRegistry catalog) : IAppTool
{
    public McpServerTool Create(ToolRunContext run) => new Session(catalog, run).Create();

    private sealed class Session(IToolCatalogRegistry catalog, ToolRunContext run)
    {
        public McpServerTool Create() => McpServerTool.Create(Search,
            new McpServerToolCreateOptions
            {
                SerializerOptions = ToolReply.Json,
                Description = "Find tools that were omitted from this turn's schema budget. Search by the capability you need. "
                              + "The matching tools become available on the next model step; do not guess an omitted tool name."
            });

        [McpServerTool(Name = "tool_search", ReadOnly = true, Destructive = false, Idempotent = true,
            OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolSearchResult))]
        private CallToolResult Search(string query, int limit = 5)
        {
            try
            {
                var matches = catalog.SearchAndPin(run, query, limit);
                return ToolReply.Of(new ToolSearchResult(matches,
                    matches.Count == 0
                        ? "No matching permitted tools were found. Continue with the tools already available."
                        : "The listed tools are pinned and will be available on the next model step."));
            }
            catch (ArgumentException error)
            {
                return ToolReply.Of(new ToolSearchResult([], "Use a short capability description and search again.", error.Message), true);
            }
        }
    }
}
