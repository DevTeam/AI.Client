namespace AI.Web.Pages;

using Microsoft.JSInterop;

public partial class Home
{
    /// <summary>Opens a granted directory of the selected project in the preview panel.</summary>
    private async Task OpenDirectoryPreviewAsync(string path)
    {
        CloseContextMenus();
        await OpenFilePreviewAsync(path);
    }

    /// <summary>Opens a directory of the selected project in the system file manager.</summary>
    private async Task OpenDirectoryInFileManagerAsync(string path)
    {
        CloseContextMenus();
        if (_selectedProject is not { } project) return;
        try
        {
            await FilePreviewApi.OpenAsync(project.Id, path, CancellationToken.None);
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException)
        {
            Notifications.ShowError(error.Message);
        }
    }

    private async Task CopyDirectoryPathAsync(string path)
    {
        CloseContextMenus();
        if (_composerModule is null) return;
        await _composerModule.InvokeVoidAsync("copyText", path);
        Notifications.ShowSuccess("Path copied.");
    }

    private async Task OpenChatFilesInFileManagerAsync()
    {
        if (await GetChatFilesDirectoryAsync() is { } path) await OpenDirectoryInFileManagerAsync(path);
        else CloseContextMenus();
    }
}
