namespace AI.Web.Pages;

using Microsoft.JSInterop;

public partial class Home
{
    private string? _linkedSkillId;
    private string? _linkedSkillSource;
    private string? _linkedToolName;
    private int _linkedSelection;

    private async Task OpenAppNavigationLinkAsync(string href)
    {
        var target = NavigationLinks.Parse(href, _selectedProject?.Id);
        if (target is null)
        {
            Notifications.ShowError("This application navigation link is invalid or unsupported.");
            return;
        }
        try
        {
            if (target.SkillId is { } skillId)
            {
                if (target.ProjectId != Guid.Empty && _projects.All(project => project.Id != target.ProjectId))
                {
                    Notifications.ShowError("The project in this link is unavailable.");
                    return;
                }
                var skills = await SkillApi.ListAsync(target.ProjectId == Guid.Empty ? null : target.ProjectId, CancellationToken.None);
                var skill = skills.Where(item => item.Id == skillId)
                    .OrderByDescending(item => item.Source == "Project" ? 2 : item.Source == "User" ? 1 : 0).FirstOrDefault();
                if (skill is null) { Notifications.ShowError("The skill in this link is unavailable."); return; }
                if (skill.Source == "Project" && target.ProjectId != _selectedProject?.Id)
                    await NavigateToTargetAsync(new WorkspaceTarget(target.ProjectId, null, null, null, false));
                var section = skill.Source == "Project" ? "ProjectSkills" : "Skills";
                if (_globalSection != section) await OpenGlobalSection(section);
                if (_globalSection != section) return;
                _linkedSkillId = skillId;
                _linkedSkillSource = skill.Source;
                _linkedSelection++;
            }
            else if (target.ToolName is { } toolName)
            {
                if (_globalSection != "MCP") await OpenGlobalSection("MCP");
                if (_globalSection != "MCP") return;
                _linkedToolName = toolName;
                _linkedSelection++;
            }
            else if (target.Target is "project" or "chat" or "branch")
            {
                await NavigateToTargetAsync(new WorkspaceTarget(target.ProjectId, target.ChatId, target.BranchId, null, false));
                await SyncWorkspaceUrlAsync();
            }
            else
            {
                if (target.ProjectId != Guid.Empty && _projects.All(project => project.Id != target.ProjectId))
                {
                    Notifications.ShowError("The project in this link is unavailable.");
                    return;
                }
                if (target.ChatId is { } chatId)
                {
                    var chat = await ChatHistoryApi.GetAsync(target.ProjectId, chatId, CancellationToken.None);
                    if (chat is null || chat.IsGuide) { Notifications.ShowError("The chat in this link is unavailable."); return; }
                }
                await RevealGuideTargetAsync(target);
                await InvokeAsync(StateHasChanged);
                var module = _guideModule ??= await JsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/appGuide.js");
                if (!await module.InvokeAsync<bool>("revealNavigationTarget", target))
                    Notifications.ShowError("The control in this link is unavailable in the current view.");
            }
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException or JSException)
        {
            Notifications.ShowError("The destination in this link could not be opened.");
        }
        await InvokeAsync(StateHasChanged);
    }
}
