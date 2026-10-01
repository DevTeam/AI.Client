namespace AI.Mcp.CSharp.Scripts;

public interface IScriptRunner
{
    Task<ScriptResult> RunAsync(ScriptRequest request, CancellationToken cancellationToken);
}
