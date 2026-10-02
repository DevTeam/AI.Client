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

    /// <summary>Explicitly selected language IDs for preview layout correction.</summary>
    public IReadOnlyList<string> TextCorrectionLanguages { get; init; } = [];

    public bool ShowContextWindowUsage { get; init; } = true;

    /// <summary>Whether the turn line shows what the turn used ("42k → 1.8k") next to its time.</summary>
    public bool ShowTurnTokens { get; init; }

    /// <summary>Whether the column of chat widgets is open beside the conversation.</summary>
    public bool ChatWidgetsOpen { get; init; }

    public bool GuideSuggestionsEnabled { get; init; } = true;
    public int GuideIdleMinutes { get; init; } = 5;
    public DateTimeOffset? GuideLastOfferedAt { get; init; }
    public IReadOnlyList<string> CompletedGuideTopics { get; init; } = [];

    /// <summary>The widgets of that column in the person's order, with what each one shows.</summary>
    public IReadOnlyList<AI.Web.Widgets.ChatWidgetPreference> ChatWidgets { get; init; } = [];
}
