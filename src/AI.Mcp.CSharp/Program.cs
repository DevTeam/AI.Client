using AI.Mcp.CSharp;
using AI.Mcp.CSharp.Scripts;
using ModelContextProtocol.Server;

var composition = new Composition();
if (args is ["--script-worker"])
{
    await ScriptWorker.RunAsync(composition.Worker);
    return;
}

await composition.Root.RunAsync();

internal partial class Program(McpServer server)
{
    private async Task RunAsync() => await server.RunAsync();
}
