namespace AI.Web.Pages;

using System.Net.Http;
using AI.Contracts.Chats;
using AI.Contracts.Projects;

/// <summary>
/// The sidebar's project tree. Any number of projects can be unfolded at once, and opening one
/// project leaves the others as they were. Every unfolded project lists the same rows: its chats a
/// page at a time and its archive. The open project reads them from <c>_chats</c> and adds the open
/// chat's branches; another lists its chats as last read from the Host, a click away without
/// switching to it first. Which projects are unfolded is remembered with the Recents state.
/// </summary>
public partial class Home
{
    private readonly HashSet<Guid> _expandedProjectIds = [];

    // The chat lists of the unfolded projects other than the open one. The open project's list is
    // _chats; the project just left keeps its last list here until the next read replaces it.
    private readonly Dictionary<Guid, IReadOnlyList<ChatSummary>> _projectChatLists = [];

    // How many chats each unfolded project shows when "Show more" has been pressed in it; one page
    // otherwise. Kept per project, so opening a project or leaving it keeps the pages it had.
    private readonly Dictionary<Guid, int> _projectChatPages = [];

    private bool IsProjectExpanded(Guid projectId) => _expandedProjectIds.Contains(projectId);

    private void ExpandProject(Guid projectId)
    {
        if (_expandedProjectIds.Add(projectId) && _recentsStateLoaded) _ = SaveRecentsStateAsync();
    }

    /// <summary>
    /// Opening a chat from anywhere (search, a notification, a new chat, a model's navigation) shows
    /// it in its project: that project unfolds — the open one by default — and lists the branches.
    /// </summary>
    private void UnfoldProjectTree(Guid? projectId = null)
    {
        _branchHost = null;
        if ((projectId ?? _selectedProject?.Id) is { } id) ExpandProject(id);
    }

    /// <summary>Folds or unfolds a project's list without opening the project.</summary>
    private async Task ToggleProjectExpandedAsync(ProjectSummary project)
    {
        if (_expandedProjectIds.Remove(project.Id))
        {
            _projectChatPages.Remove(project.Id);
        }
        else
        {
            _expandedProjectIds.Add(project.Id);
        }
        await SaveRecentsStateAsync();
        if (IsProjectExpanded(project.Id) && project.Id != _selectedProject?.Id && !_projectChatLists.ContainsKey(project.Id))
            await LoadProjectChatsAsync(project.Id);
    }

    /// <summary>The chats another unfolded project lists, in the Host's order: pinned first, then by activity.</summary>
    private List<ChatSummary>? GetOtherProjectChats(Guid projectId) =>
        _projectChatLists.TryGetValue(projectId, out var chats)
            ? chats.Where(chat => chat.ArchivedAt is null && !chat.IsEmpty).ToList()
            : null;

    private int GetOtherProjectArchivedCount(Guid projectId) =>
        _projectChatLists.TryGetValue(projectId, out var chats) ? chats.Count(chat => chat.ArchivedAt is not null) : 0;

    private int GetChatPageCount(Guid projectId) => _projectChatPages.GetValueOrDefault(projectId, ChatPageSize);

    private void ShowMoreChats(Guid projectId, int visibleCount) => _projectChatPages[projectId] = visibleCount + ChatPageSize;

    private void ShowFewerChats(Guid projectId) => _projectChatPages.Remove(projectId);

    /// <summary>
    /// The open chat when it lies under its project's "Show more": it stays listed, below the pager,
    /// rather than pulling the page open, since a page that grows on every pick changes the counts.
    /// </summary>
    private ChatSummary? GetPagedAwayActiveChat(IReadOnlyList<ChatSummary> chats, int visibleCount) =>
        GetActiveChatId() is { } activeChatId ? chats.Skip(visibleCount).FirstOrDefault(chat => chat.Id == activeChatId) : null;

    /// <summary>The Archive row of any unfolded project: another project is opened first, since the archive works on the open one.</summary>
    private async Task OpenProjectArchiveAsync(ProjectSummary project)
    {
        if (_selectedProject?.Id != project.Id) await SelectProjectAsync(project);
        if (_selectedProject?.Id == project.Id) OpenArchive(false);
    }

    /// <summary>Re-reads the chat lists of the unfolded projects other than the open one.</summary>
    private Task RefreshExpandedProjectChatsAsync() => Task.WhenAll(_expandedProjectIds
        .Where(id => id != _selectedProject?.Id && _projects.Any(project => project.Id == id))
        .ToList()
        .Select(LoadProjectChatsAsync));

    private async Task LoadProjectChatsAsync(Guid projectId)
    {
        try { _projectChatLists[projectId] = await ChatHistoryApi.ListAsync(projectId, CancellationToken.None); }
        // Unreachable Host: the event stream reports the outage; the list it had stays.
        catch (HttpRequestException) { }
    }
}
