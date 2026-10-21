namespace AI.Mcp.BuiltIn.Scripts;

public interface IScriptRunner
{
    Task<ScriptResult> RunAsync(ScriptRequest request, CancellationToken cancellationToken);
}
