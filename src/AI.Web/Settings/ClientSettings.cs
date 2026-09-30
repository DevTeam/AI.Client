namespace AI.Web.Settings;

/// <summary>
/// Preferences of this app on this device, as opposed to <see cref="IGlobalSettingsApi"/>, which
/// the Host owns and every client shares. A new preference is a new property with a default, so
/// an entry written by an older build still reads.
/// </summary>
public sealed record ClientSettings
{
    public ThemePreference Theme { get; init; } = ThemePreference.System;

    public AccentColor Accent { get; init; } = AccentColor.Blue;

    public int CornerRoundnessPercent { get; init; } = 100;

    public bool NotificationSoundEnabled { get; init; } = true;

    public bool OtherSoundsEnabled { get; init; } = true;

    public bool ShowContextWindowUsage { get; init; } = true;
}
