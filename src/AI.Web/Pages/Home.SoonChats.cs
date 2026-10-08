namespace AI.Web.Pages;

using System.Net.Http;
using AI.Contracts.Chats;
using AI.Contracts.Schedules;
using Microsoft.AspNetCore.Components.Web;

/// <summary>
/// The sidebar's Scheduled: the scheduled chats that come due within the next day, across every
/// project, the soonest first, and under them the chats whose schedule has finished its work, the
/// run that ended last first. A row says how long is left before its run starts; a chat that has
/// finished keeps its row, without a countdown, pushed down by the newer ones until it falls past
/// the window the section remembers. The section folds and pages like Recents, and it is absent
/// while there is nothing to show.
/// </summary>
public partial class Home
{
    // How many show at first is a setting; "Show more" opens at least ten, as Recents does.
    private int _soonChatCount = Settings.ClientSettings.DefaultSoonChatCount;
    private int SoonChatsShown => Math.Clamp(_soonChatCount,
        Settings.ClientSettings.MinSoonChatCount, Settings.ClientSettings.MaxSoonChatCount);
    private int SoonChatsMore => Math.Max(10, SoonChatsShown * 2);

    private readonly List<ScheduledChatSummary> _soonChats = [];
    private int _soonChatsRequest;
    private bool _soonFolded;
    private bool _soonShowMore;

    /// <summary>
    /// The scheduled chats read from the Host, soonest first and finished last, with the chat opened
    /// from this section on top: its row cannot vanish from under the pointer once newer chats have
    /// pushed it out of the window, exactly as Notifications keeps the chat opened from it.
    /// </summary>
    private List<ScheduledChatSummary> GetSoonChats()
    {
        if (!IsFoldedInto(ChatListSurface.Soon) || GetActiveChatId() is not { } opened
            || _soonChats.Any(item => item.Chat.Id == opened)
            || _chats.FirstOrDefault(chat => chat.Id == opened) is not { Kind: ChatSchedule.Kind } chat) return _soonChats;
        return [new ScheduledChatSummary(chat, null), .. _soonChats];
    }

    /// <summary>
    /// The chats the section shows at this moment: the first few, or the longer list once opened,
    /// and none while the section is folded.
    /// </summary>
    private List<ScheduledChatSummary> GetVisibleSoonChats() => _soonFolded
        ? []
        : GetSoonChats().Take(_soonShowMore ? GetSoonChats().Count : SoonChatsShown).ToList();

    /// <summary>
    /// "now", "~33s", "~5m", "~3h": how long is left before the chat's run starts, and nothing for
    /// a chat whose schedule has finished its work — a finished row has no countdown to run down.
    /// </summary>
    private static string? GetSoonLead(DateTimeOffset? dueAt, DateTimeOffset now)
    {
        if (dueAt is not { } due) return null;
        var left = due - now;
        if (left <= TimeSpan.Zero) return "now";
        if (left < TimeSpan.FromMinutes(1)) return $"~{Math.Ceiling(left.TotalSeconds):0}s";
        if (left < TimeSpan.FromHours(1)) return $"~{left.Minutes}m";
        if (left < TimeSpan.FromDays(1)) return $"~{(int)left.TotalHours}h";
        return $"~{(int)left.TotalDays}d";
    }

    /// <summary>Reads the scheduled chats the section lists; the Host's own countdown is not needed.</summary>
    private async Task RefreshSoonChatsAsync()
    {
        var request = ++_soonChatsRequest;
        IReadOnlyList<ScheduledChatSummary> soon;
        try { soon = await ScheduleApi.ListSoonAsync(SoonChatsMore, CancellationToken.None); }
        // Unreachable Host: the event stream reports the outage itself, and the list it had stays.
        catch (HttpRequestException) { return; }
        // A slower, older answer must not replace a newer one.
        if (request != _soonChatsRequest) return;
        _soonChats.Clear();
        _soonChats.AddRange(soon);
    }

    private void RefreshSoonChatsInBackground() => _ = InvokeAsync(async () =>
    {
        await RefreshSoonChatsAsync();
        StateHasChanged();
    });

    private Task OpenSoonChatRowAsync(ChatSummary chat) => OpenChatFromListAsync(chat, ChatListSurface.Soon);

    private void SetSoonChatCount(int count)
    {
        var fetched = SoonChatsMore;
        _soonChatCount = count;
        // A longer list than the Host was last asked for has to be read again.
        if (SoonChatsMore > fetched) RefreshSoonChatsInBackground();
    }

    private Task ToggleSoonFoldedAsync()
    {
        _soonFolded = !_soonFolded;
        return SaveRecentsStateAsync();
    }

    private Task ToggleSoonShowMoreAsync()
    {
        _soonShowMore = !_soonShowMore;
        return SaveRecentsStateAsync();
    }

    /// <summary>
    /// Escape on the Scheduled heading folds the section and hands the keyboard back to the open
    /// project, so the tree is where the person was rather than nowhere.
    /// </summary>
    private async Task OnScheduledToggleKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs args)
    {
        if (args.Key != "Escape" || _soonFolded) return;
        await ToggleSoonFoldedAsync();
        await WorkspaceLayoutService.FocusAdjacentItemAsync(".project-nav-row", ".project-link", "Home");
    }
}
