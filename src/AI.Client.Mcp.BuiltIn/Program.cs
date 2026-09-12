using AI.Client.Mcp.BuiltIn;
using ModelContextProtocol.Server;

var composition = new Composition();
await composition.Root.RunAsync();

internal partial class Program(McpServer server)
{
    private async Task RunAsync() => await server.RunAsync();
}