namespace AI.Web.Pages;

using System.Net.Http;
using AI.Contracts.Chats;
using AI.Contracts.Schedules;
using Microsoft.AspNetCore.Components.Web;

/// <summary>
/// The sidebar's Scheduled: the scheduled chats the Host will act on within the next day, across
/// every project, the soonest first. A row says how long is left before its run starts, the section
/// folds and pages like Recents, and it is absent while nothing is coming.
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

    /// <summary>The scheduled chats read from the Host, soonest first.</summary>
    private List<ScheduledChatSummary> GetSoonChats() => _soonChats;

    /// <summary>
    /// The chats the section shows at this moment: the first few, or the longer list once opened,
    /// and none while the section is folded.
    /// </summary>
    private List<ScheduledChatSummary> GetVisibleSoonChats() => _soonFolded
        ? []
        : _soonChats.Take(_soonShowMore ? _soonChats.Count : SoonChatsShown).ToList();

    /// <summary>"now", "~33s", "~5m", "~3h": how long is left before the chat's run starts.</summary>
    private static string GetSoonLead(DateTimeOffset dueAt, DateTimeOffset now)
    {
        var left = dueAt - now;
        if (left <= TimeSpan.Zero) return "now";
        if (left < TimeSpan.FromMinutes(1)) return $"~{Math.Ceiling(left.TotalSeconds):0}s";
        if (left < TimeSpan.FromHours(1)) return $"~{left.Minutes}m";
        if (left < TimeSpan.FromDays(1)) return $"~{(int)left.TotalHours}h";
        return $"~{(int)left.TotalDays}d";
    }

    /// <summary>Reads the scheduled chats that come due soon; the Host's own countdown is not needed.</summary>
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
