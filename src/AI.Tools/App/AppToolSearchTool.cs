namespace AI.Mcp.App;

using AI.Application.Tools;
using AI.Contracts.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

public sealed record ToolSearchResult(IReadOnlyList<ToolCatalogMatch> Tools, string Guidance, int Remaining = 0, string? Error = null);

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
                Description = $"Call {ToolRef.ToolSearchName} when the capability needed for the user's task is absent from the visible tool list. "
                              + "Search with 1-5 English words, a verb and an object such as 'read text file'; server limits "
                              + "it to one MCP server. The best matches are prioritized for the next model step when their "
                              + "schemas fit. Do not guess an omitted tool name."
            });

        [McpServerTool(Name = "tool_search", ReadOnly = true, Destructive = false, Idempotent = true,
            OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolSearchResult))]
        private CallToolResult Search(string query, int limit = 5, string? server = null)
        {
            try
            {
                var found = catalog.SearchAndPin(run, query, limit, server);
                return reply.Reply(new ToolSearchResult(found.Tools,
                    found.Tools.Count > 0
                        ? "Matches are listed best first. Those with prioritized=true are prioritized for the next model step within "
                          + "its schema budget; search another listed name exactly to prioritize it."
                          + (found.Remaining > 0 ? $" {found.Remaining} more match; search more narrowly." : string.Empty)
                        : AsksForDrawing(query)
                            ? "Diagrams need no tool: write a ```mermaid or ```svg block in your answer and the chat draws it."
                            : "No matching permitted tools were found. Retry with other English words, or continue with the tools already available.",
                    found.Remaining));
            }
            catch (ArgumentException error)
            {
                return reply.Reply(new ToolSearchResult([], "Use a short English capability description and search again.",
                    Error: error.Message), true);
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
