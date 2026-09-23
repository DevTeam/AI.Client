namespace AI.Client.Mcp.App;

using AI.Client.Application.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

public sealed record ToolSearchResult(IReadOnlyList<ToolCatalogMatch> Tools, string Guidance, string? Error = null);

[McpServerToolType]
public sealed class AppToolSearchTool(IToolCatalogRegistry catalog) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => new Session(catalog, run, reply).Create();

    private sealed class Session(IToolCatalogRegistry catalog, ToolRunContext run, IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(Search,
            new McpServerToolCreateOptions
            {
                SerializerOptions = reply.Json,
                Description = "Call app_tool_search when the capability needed for the user's task is absent from the visible tool list. "
                              + "Search with a short English capability description. Tools may have been omitted from this turn's schema "
                              + "budget; matching permitted tools become available on the next model step. Do not guess an omitted tool name."
            });

        [McpServerTool(Name = "tool_search", ReadOnly = true, Destructive = false, Idempotent = true,
            OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolSearchResult))]
        private CallToolResult Search(string query, int limit = 5)
        {
            try
            {
                var matches = catalog.SearchAndPin(run, query, limit);
                return reply.Reply(new ToolSearchResult(matches,
                    matches.Count == 0
                        ? "No matching permitted tools were found. Continue with the tools already available."
                        : "The listed tools are pinned and will be available on the next model step."));
            }
            catch (ArgumentException error)
            {
                return reply.Reply(new ToolSearchResult([], "Use a short capability description and search again.", error.Message), true);
            }
        }
    }
}
