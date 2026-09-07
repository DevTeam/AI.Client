using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using AI.Client.Mcp.BuiltIn;

var processRunTool = McpServerTool.Create(ProcessRunTool.RunAsync);

await using var server = McpServer.Create(
    new StdioServerTransport("Built‑in"),
    new McpServerOptions
    {
        ServerInfo = new Implementation { Name = "Built‑in", Version = "1.0.0" },
        ToolCollection = [processRunTool]
    });

await server.RunAsync();
