namespace AI.Web.Pages;

using Microsoft.JSInterop;

public partial class Home
{
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
            if (target.Target is "project" or "chat" or "branch")
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
