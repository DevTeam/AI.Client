namespace AI.Desktop;

/// <summary>Keeps the window's placement between runs.</summary>
internal interface IWindowPlacementStore
{
    /// <returns>The last saved placement, or null when there is none or it cannot be read.</returns>
    WindowPlacement? Load();

    void Save(WindowPlacement placement);
}
