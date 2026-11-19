namespace AI.Application.Tools;

public interface IToolCatalogRegistry
{
    IDisposable Begin(ToolRunContext run);
    void Update(ToolRunContext run, IReadOnlyList<AgentTool> tools);
    IReadOnlySet<string> GetPinned(ToolRunContext run);
    /// <summary>Consumes pending discovery priorities for one selection step, including all searches in a batch.</summary>
    IReadOnlySet<string> ConsumePinned(ToolRunContext run);
    /// <summary>
    /// Ranks the run's tools for an English query, optionally on one server, and pins the best
    /// matches. A query exactly naming a tool returns that tool alone.
    /// </summary>
    ToolCatalogSearch SearchAndPin(ToolRunContext run, string query, int limit, string? server = null);

    /// <summary>
    /// Whether the run's next searches hand back the best matches' schemas, to be called through
    /// <see cref="AI.Contracts.Tools.ToolRef.CallToolName"/>, instead of pinning them into the tool list.
    /// </summary>
    void SetDeferred(ToolRunContext run, bool deferred);

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

/// <param name="Prioritized">Whether the match is pinned for the next model step; others can be pinned by searching their exact name.</param>
/// <param name="InputSchema">The match's full input schema when it is to be called through the stand-in rather than pinned.</param>
public sealed record ToolCatalogMatch(string Name, string Description, string ServerId, bool Prioritized,
    System.Text.Json.JsonElement? InputSchema = null);

/// <param name="Remaining">How many further tools match beyond the ones returned.</param>
/// <param name="Deferred">Whether the matches with a schema are called through the stand-in instead of joining the tool list.</param>
public sealed record ToolCatalogSearch(IReadOnlyList<ToolCatalogMatch> Tools, int Remaining, bool Deferred = false);
