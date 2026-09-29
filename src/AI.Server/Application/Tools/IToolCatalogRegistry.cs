namespace AI.Application.Tools;

public interface IToolCatalogRegistry
{
    IDisposable Begin(ToolRunContext run);
    void Update(ToolRunContext run, IReadOnlyList<AgentTool> tools);
    IReadOnlySet<string> GetPinned(ToolRunContext run);
    IReadOnlyList<ToolCatalogMatch> SearchAndPin(ToolRunContext run, string query, int limit);

    /// <summary>
    /// Pins the run's tools with these names, given with or without their server prefix, so they
    /// are in the next model step's schema. Returns the full names that were pinned.
    /// </summary>
    IReadOnlyList<string> Pin(ToolRunContext run, IEnumerable<string> names);
}

public sealed record ToolCatalogMatch(string Name, string Description, string ServerId);
