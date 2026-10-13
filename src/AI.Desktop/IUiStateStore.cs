namespace AI.Desktop;

/// <summary>
/// The page's own remembered state — sidebar layout, the project tree's folds, drafts — which the
/// browser keeps per origin. The embedded server's port, and with it the origin, changes on every
/// start, so the desktop keeps a copy and hands it back before the page starts.
/// </summary>
internal interface IUiStateStore
{
    /// <summary>A JSON object of storage keys and their values, or null before the first save.</summary>
    string? Load();

    void Save(string json);
}
