namespace AI.Web.Pages;

using System.Net.Http;
using AI.Contracts.Chats;
using AI.Contracts.Projects;

/// <summary>
/// The sidebar's project tree. Any number of projects can be unfolded at once, and opening one
/// project leaves the others as they were. The open project lists its chats from <c>_chats</c>, with
/// the open chat's branches, its archive and its directories; another unfolded project lists its
/// latest chats as last read from the Host, a click away without switching to it first. Which
/// projects are unfolded is remembered with the Recents state.
/// </summary>
public partial class Home
{
    // A few chats of a project that is not open; the rest a click away, and all of them once open.
    private const int OtherProjectChatsShown = 5;

    private readonly HashSet<Guid> _expandedProjectIds = [];

    // The chat lists of the unfolded projects other than the open one. The open project's list is
    // _chats; the project just left keeps its last list here until the next read replaces it.
    private readonly Dictionary<Guid, IReadOnlyList<ChatSummary>> _projectChatLists = [];

    // The unfolded projects whose whole chat list is shown rather than the first few.
    private readonly HashSet<Guid> _projectChatsShowAll = [];

    private bool IsProjectExpanded(Guid projectId) => _expandedProjectIds.Contains(projectId);

    private void ExpandProject(Guid projectId)
    {
        if (_expandedProjectIds.Add(projectId) && _recentsStateLoaded) _ = SaveRecentsStateAsync();
    }

    /// <summary>
    /// Opening something other than a Recents row (search, a notification, a new chat, a model's
    /// navigation) shows the chat in its project: that project unfolds — the open one by default.
    /// </summary>
    private void UnfoldProjectTree(Guid? projectId = null)
    {
        _openedFrom = null;
        if ((projectId ?? _selectedProject?.Id) is { } id) ExpandProject(id);
    }

    /// <summary>Folds or unfolds a project's list without opening the project.</summary>
    private async Task ToggleProjectExpandedAsync(ProjectSummary project)
    {
        if (_expandedProjectIds.Remove(project.Id))
        {
            _projectChatsShowAll.Remove(project.Id);
        }
        else
        {
            _expandedProjectIds.Add(project.Id);
            // The open chat's row moves back into its project with its branches.
            if (project.Id == _selectedProject?.Id) _openedFrom = null;
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

    private void ToggleProjectChatsShowAll(Guid projectId)
    {
        if (!_projectChatsShowAll.Remove(projectId)) _projectChatsShowAll.Add(projectId);
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
