namespace AI.Client.Desktop;

/// <summary>
/// Keeps the theme the page last asked for, so the next run paints the titlebar in it before the
/// page has loaded and said so again.
/// </summary>
internal interface IThemePreferenceStore
{
    /// <returns>"system", "light" or "dark"; "system" when nothing readable is saved.</returns>
    string Load();

    void Save(string preference);
}
