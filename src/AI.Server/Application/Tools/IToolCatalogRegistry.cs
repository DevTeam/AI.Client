namespace AI.Application.Tools;

public interface IToolCatalogRegistry
{
    IDisposable Begin(ToolRunContext run);
    void Update(ToolRunContext run, IReadOnlyList<AgentTool> tools);
    IReadOnlySet<string> GetPinned(ToolRunContext run);
    /// <summary>Consumes pending discovery priorities for one selection step, including all searches in a batch.</summary>
    IReadOnlySet<string> ConsumePinned(ToolRunContext run);
    IReadOnlyList<ToolCatalogMatch> SearchAndPin(ToolRunContext run, string query, int limit);

    /// <summary>
    /// Pins the run's tools with these names, given with or without their server prefix, so they
    /// are prioritized for the next model step's schema budget. Returns the full names that were pinned.
    /// </summary>
    IReadOnlyList<string> Pin(ToolRunContext run, IEnumerable<string> names);

    /// <summary>
    /// Records that the run was handed this playbook with these arguments. False when it already
    /// was: the model has its instructions and is starting the same work over.
    /// </summary>
    bool TryRecordPlaybook(ToolRunContext run, string skillId, string arguments);
}

public sealed record ToolCatalogMatch(string Name, string Description, string ServerId);
