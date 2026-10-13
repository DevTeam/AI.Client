namespace AI.Web.Pages;

using Microsoft.JSInterop;

public partial class Home
{
    /// <summary>
    /// The menu id of the "Chat files" row. That row is no stored grant, so it borrows a fixed id
    /// that no grant can have, and every place that closes the directory menu closes it too.
    /// </summary>
    private static readonly Guid ChatFilesMenuId = new("6a1c7f52-0d4e-4b0a-9a51-2f3c1e7d9b40");

    /// <summary>Opens the scratch directory of the open chat in the preview panel.</summary>
    private async Task OpenChatFilesAsync()
    {
        CloseContextMenus();
        if (await GetChatFilesDirectoryAsync() is { } path) await OpenFilePreviewAsync(path);
    }

    private async Task CopyChatFilesPathAsync()
    {
        CloseContextMenus();
        if (_composerModule is null || await GetChatFilesDirectoryAsync() is not { } path) return;
        await _composerModule.InvokeVoidAsync("copyText", path);
        Notifications.ShowSuccess("Path copied.");
    }

    /// <summary>
    /// The scratch directory of the chat open in the selected project, created on first use; null,
    /// with the reason shown, when no chat of the project is open or the Host refused.
    /// </summary>
    private async Task<string?> GetChatFilesDirectoryAsync()
    {
        if (_selectedProject is not { } project || _selectedChat is not { } chat || chat.ProjectId != project.Id)
        {
            Notifications.ShowInfo("Open a chat to see its files.");
            return null;
        }
        try
        {
            return await ResourceApi.GetChatTemporaryDirectoryAsync(project.Id, chat.Id, CancellationToken.None);
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException)
        {
            Notifications.ShowError("The chat's files could not be opened. Try again.");
            return null;
        }
    }
}
