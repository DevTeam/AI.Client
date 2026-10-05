namespace AI.Web.Pages;

using AI.Contracts.Resources;
using AI.Web.Components;

public partial class Home
{
    private string? _previewPath;
    private ChatResource? _previewAsset;
    private Guid _previewProjectId;
    private int? _previewStartLine, _previewEndLine;
    private long _previewVersion;

    private Task OpenFilePreviewAsync(string path) => OpenFileLinkPreviewAsync(new FilePreviewTarget(path));

    private async Task OpenFileLinkPreviewAsync(FilePreviewTarget target)
    {
        if (_selectedProject is null || !await CloseDrawersExceptAsync(Drawer.FilePreview)) return;
        CloseContextMenus();
        _previewProjectId = _selectedProject.Id;
        _previewAsset = null;
        _previewPath = target.Path;
        _previewStartLine = target.StartLine;
        _previewEndLine = target.EndLine;
        _previewVersion++;
    }

    private async Task OpenAssetPreviewAsync(ChatResource resource)
    {
        if (resource.Kind is not (ChatResourceKind.File or ChatResourceKind.Image) || resource.AssetId is null
            || _selectedProject is null || !await CloseDrawersExceptAsync(Drawer.FilePreview)) return;
        CloseContextMenus();
        _previewProjectId = _selectedProject.Id;
        _previewPath = null;
        _previewAsset = resource;
        _previewStartLine = _previewEndLine = null;
        _previewVersion++;
    }

    private void CloseFilePreview()
    {
        _previewPath = null;
        _previewAsset = null;
    }

    private Task AttachPreviewAsync(FilePreview file) => AddDraftResourceAsync(
        [file.Kind == "directory" ? ChatResourceKind.Directory : ChatResourceKind.File], file.Path);

    private void AttachPreviewAsset(ChatResource resource)
    {
        if (_selectedProject?.Id != _previewProjectId) return;
        if (_draftResources.Count >= 20)
        {
            Notifications.ShowError("A message can reference at most 20 resources.");
            return;
        }
        _draftResources.Add(resource with { Id = Guid.CreateVersion7(), Mention = null });
        _resourceDrafts[GetComposerDraftKey()] = _draftResources.ToArray();
    }
}
