namespace AI.Web.Pages;

using AI.Contracts.Resources;

public partial class Home
{
    private string? _previewPath;
    private Guid _previewProjectId;

    private async Task OpenFilePreviewAsync(string path)
    {
        if (_selectedProject is null || !await CloseDrawersExceptAsync(Drawer.FilePreview)) return;
        CloseContextMenus();
        _previewProjectId = _selectedProject.Id;
        _previewPath = path;
    }

    private void CloseFilePreview() => _previewPath = null;

    private Task AttachPreviewAsync(FilePreview file) => AddDraftResourceAsync(
        [file.Kind == "directory" ? ChatResourceKind.Directory : ChatResourceKind.File], file.Path);
}
