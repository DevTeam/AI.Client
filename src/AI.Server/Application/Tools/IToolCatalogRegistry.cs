namespace AI.Application.Tools;

public interface IToolCatalogRegistry
{
    IDisposable Begin(ToolRunContext run);
    void Update(ToolRunContext run, IReadOnlyList<AgentTool> tools);
    IReadOnlySet<string> GetPinned(ToolRunContext run);
    IReadOnlyList<ToolCatalogMatch> SearchAndPin(ToolRunContext run, string query, int limit);
}

public sealed record ToolCatalogMatch(string Name, string Description, string ServerId);
