namespace AI.Web.Pages;

using AI.Contracts.Resources;
using AI.Web.Components;

public partial class Home
{
    private string? _previewPath;
    private Guid _previewProjectId;
    private int? _previewStartLine, _previewEndLine;
    private long _previewVersion;

    private Task OpenFilePreviewAsync(string path) => OpenFileLinkPreviewAsync(new FilePreviewTarget(path));

    private async Task OpenFileLinkPreviewAsync(FilePreviewTarget target)
    {
        if (_selectedProject is null || !await CloseDrawersExceptAsync(Drawer.FilePreview)) return;
        CloseContextMenus();
        _previewProjectId = _selectedProject.Id;
        _previewPath = target.Path;
        _previewStartLine = target.StartLine;
        _previewEndLine = target.EndLine;
        _previewVersion++;
    }

    private void CloseFilePreview() => _previewPath = null;

    private Task AttachPreviewAsync(FilePreview file) => AddDraftResourceAsync(
        [file.Kind == "directory" ? ChatResourceKind.Directory : ChatResourceKind.File], file.Path);
}
