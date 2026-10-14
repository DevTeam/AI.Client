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

    /// <summary>Whether the interface moves: transitions, arrivals, sheens and glides. Off, every change is instant.</summary>
    public bool AnimationsEnabled { get; init; } = true;

    public bool NotificationSoundEnabled { get; init; } = true;

    public bool OtherSoundsEnabled { get; init; } = true;

    /// <summary>Explicitly selected language IDs for spelling and keyboard layout correction.</summary>
    public IReadOnlyList<string> TextCorrectionLanguages { get; init; } = [];

    /// <summary>The quick switch beside the message editor; off pauses correction but keeps the languages.</summary>
    public bool TextCorrectionEnabled { get; init; } = true;

    /// <summary>Whether the turn line shows what the turn used ("42k → 1.8k") next to its time.</summary>
    public bool ShowTurnTokens { get; init; }

    public const int MinLiveNoteCount = 1;
    public const int MaxLiveNoteCount = 5;
    public const int DefaultLiveNoteCount = 3;

    /// <summary>How many of a running turn's latest notes stand under its row, newest at the bottom.</summary>
    public int LiveNoteCount { get; init; } = DefaultLiveNoteCount;

    // Zero hides the section.
    public const int MinRecentChatCount = 0;
    public const int MaxRecentChatCount = 10;
    public const int DefaultRecentChatCount = 3;

    /// <summary>How many chats the sidebar's Recents shows before "Show more"; zero hides the section.</summary>
    public int RecentChatCount { get; init; } = DefaultRecentChatCount;

    // Zero hides the section.
    public const int MinSoonChatCount = 0;
    public const int MaxSoonChatCount = 10;
    public const int DefaultSoonChatCount = 3;

    /// <summary>How many scheduled chats the sidebar's Scheduled shows before "Show more"; zero hides the section.</summary>
    public int SoonChatCount { get; init; } = DefaultSoonChatCount;

    /// <summary>Whether the column of chat widgets is open beside the conversation.</summary>
    public bool ChatWidgetsOpen { get; init; }

    /// <summary>
    /// Side panel widths and whether the sidebar is hidden. Kept here rather than in a separate
    /// localStorage entry so that Desktop restores them from its profile: its embedded server gets
    /// a new port, and so a new localStorage origin, on each start.
    /// </summary>
    public AI.Web.Layout.WorkspacePanels? WorkspacePanels { get; init; }

    public bool GuideSuggestionsEnabled { get; init; } = true;
    public int GuideIdleMinutes { get; init; } = 5;
    public DateTimeOffset? GuideLastOfferedAt { get; init; }
    public IReadOnlyList<string> CompletedGuideTopics { get; init; } = [];

    /// <summary>What happens to the work a restart of the application stopped.</summary>
    public RestartRecoveryMode AfterRestart { get; init; } = RestartRecoveryMode.Ask;

    /// <summary>
    /// The start of the Host whose stopped work has been dealt with on this device: resumed, left
    /// stopped or dismissed. Work a later start recovers is offered again.
    /// </summary>
    public DateTimeOffset? RestartSeenAt { get; init; }

    /// <summary>The widgets of that column in the person's order, with what each one shows.</summary>
    public IReadOnlyList<AI.Web.Widgets.ChatWidgetPreference> ChatWidgets { get; init; } = [];
}

/// <summary>What the client does with the work a restart of the application stopped.</summary>
public enum RestartRecoveryMode
{
    /// <summary>Points it out above the composer and in Unfinished work, and waits for the person.</summary>
    Ask,

    /// <summary>Resumes it after a short countdown that can be cancelled; a tool call with an unknown outcome still waits.</summary>
    ResumeAutomatically,

    /// <summary>Says nothing: the work stays stopped and is listed in Unfinished work.</summary>
    LeaveStopped
}
