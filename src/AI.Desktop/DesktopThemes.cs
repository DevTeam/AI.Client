namespace AI.Desktop;

using Avalonia.Styling;

/// <summary>
/// The theme variants this window can paint its own chrome and drawn titlebar in. Light and Dark
/// are Avalonia's; Dark blue is the app's own, a <see cref="ThemeVariant"/> derived from
/// <see cref="ThemeVariant.Dark"/> so every key the built-in Fluent theme does not define for it
/// still resolves to the dark value.
/// </summary>
internal static class DesktopThemes
{
    /// <remarks>
    /// The key matches ThemePreference.DarkBlue lowercased, which is what the page sends over the
    /// bridge. XAML cannot name a custom variant as a string, so App.axaml keys its dictionary with
    /// <c>x:Static</c> on this property rather than with the name.
    /// </remarks>
    public static ThemeVariant DarkBlue { get; } = new("DarkBlue", ThemeVariant.Dark);
}
