namespace AI.Web.Settings;

/// <summary>Which palette the app paints with; <see cref="System"/> follows the OS and keeps following it.</summary>
public enum ThemePreference
{
    System,
    Light,
    Dark,

    /// <summary>A dark palette tinted blue: the surfaces move towards blue, the accent stays the user's.</summary>
    DarkBlue
}
