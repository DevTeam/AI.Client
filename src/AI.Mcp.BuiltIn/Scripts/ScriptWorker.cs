namespace AI.Mcp.BuiltIn.Scripts;

using System.Text.Json;

/// <summary>
/// Executes one request received from the MCP server and writes one result. The compiler is
/// warmed up before the request is read: a worker is usually started ahead of its script, so that
/// work is done while the model is still writing the code.
/// </summary>
internal static class ScriptWorker
{
    public static async Task RunAsync(ScriptRunner runner)
    {
        await runner.WarmUpAsync();
        var request = await JsonSerializer.DeserializeAsync<ScriptRequest>(Console.OpenStandardInput())
                      ?? throw new InvalidDataException("Script worker received no request.");
        var result = await runner.RunAsync(request, CancellationToken.None);
        await JsonSerializer.SerializeAsync(Console.OpenStandardOutput(), result);
    }
}
