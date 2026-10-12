namespace AI.Mcp.CSharp.Scripts;

using System.Text.Json;

/// <summary>Executes one request received from the MCP server and writes one result.</summary>
internal static class ScriptWorker
{
    public static async Task RunAsync(IScriptRunner runner)
    {
        var request = await JsonSerializer.DeserializeAsync<ScriptRequest>(Console.OpenStandardInput())
                      ?? throw new InvalidDataException("Script worker received no request.");
        var result = await runner.RunAsync(request, CancellationToken.None);
        await JsonSerializer.SerializeAsync(Console.OpenStandardOutput(), result);
    }
}
