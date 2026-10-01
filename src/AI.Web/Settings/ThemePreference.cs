namespace AI.Web.Settings;

/// <summary>Which palette the app paints with; <see cref="System"/> follows the OS and keeps following it.</summary>
public enum ThemePreference
{
    System,
    Light,
    Dark,

    /// <summary>A dark palette tinted blue: the surfaces move towards blue, the accent stays the user's.</summary>
    DarkBlue,

    /// <summary>A dark, neutral grey palette: surfaces stay in step with each other rather than fading to near-black, so the workspace reads grey rather than blue or almost-black. The accent stays the user's.</summary>
    Gray,

    /// <summary>A light, soft grey palette: surfaces stay clearly visible against each other rather than fading to white, so the workspace reads grey rather than near-white. The accent stays the user's.</summary>
    LightGray
}
