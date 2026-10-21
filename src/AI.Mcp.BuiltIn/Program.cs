using AI.Mcp.BuiltIn;
using AI.Mcp.BuiltIn.Scripts;
using ModelContextProtocol.Server;

var composition = new Composition();
if (args is [ScriptWorkers.WorkerArgument])
{
    await ScriptWorker.RunAsync(composition.Worker);
    return;
}

await composition.Root.RunAsync();

internal partial class Program(McpServer server)
{
    private async Task RunAsync() => await server.RunAsync();
}
