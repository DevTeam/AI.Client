namespace AI.Web.Components;

using AI.Contracts.Schedules;

/// <summary>
/// One line the branch picker offers. <paramref name="Key"/> is the feed's own option, handed back
/// when the line is chosen; a scheduled run carries its outcome so the picker can group, filter and
/// colour it instead of repeating the outcome in the title. A teammate's line carries the member
/// so the picker shows the same colour dot as the sidebar; the chat's own line is marked as main.
/// </summary>
public sealed record BranchPickerItem(
    object Key,
    string Title,
    string Icon,
    string? IconClass,
    string? Tooltip,
    bool Selected,
    bool EndsHere,
    ScheduleRunStatus? RunStatus = null,
    int RunNumber = 0,
    string? RunTag = null,
    AI.Contracts.Chats.TeamMember? Member = null,
    bool IsMain = false);
