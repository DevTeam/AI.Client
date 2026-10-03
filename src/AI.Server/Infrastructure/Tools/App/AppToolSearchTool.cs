namespace AI.Mcp.App;

using AI.Application.Tools;
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
                              + "budget; matching permitted tools are prioritized for the next model step when their schemas fit. "
                              + "Search narrowly if not all matches fit. Do not guess an omitted tool name."
            });

        [McpServerTool(Name = "tool_search", ReadOnly = true, Destructive = false, Idempotent = true,
            OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolSearchResult))]
        private CallToolResult Search(string query, int limit = 5)
        {
            try
            {
                var matches = catalog.SearchAndPin(run, query, limit);
                return reply.Reply(new ToolSearchResult(matches,
                    matches.Count > 0
                        ? "The listed tools are prioritized for the next model step within its schema budget. Search narrowly if not all matches fit."
                        : AsksForDrawing(query)
                            ? "Diagrams need no tool: write a ```mermaid or ```svg block in your answer and the chat draws it."
                            : "No matching permitted tools were found. Continue with the tools already available."));
            }
            catch (ArgumentException error)
            {
                return reply.Reply(new ToolSearchResult([], "Use a short capability description and search again.", error.Message), true);
            }
        }
    }

    /// <summary>
    /// A search for a drawing tool is the moment a model is about to conclude that it cannot draw,
    /// so an empty result for one says how the chat draws instead.
    /// </summary>
    private static bool AsksForDrawing(string query) =>
        DrawingWords.Any(word => query.Contains(word, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] DrawingWords =
        ["diagram", "chart", "draw", "svg", "mermaid", "flowchart", "graph", "plot", "sketch", "visuali"];
}
