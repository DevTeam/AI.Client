namespace AI.Web.Components.FilePreviews;

using AI.Contracts.Resources;
using Microsoft.AspNetCore.Components;

public sealed record FilePreviewViewContext(Guid ProjectId, FilePreview File, string? ContentUrl,
    string PanelId, int? StartLine, int? EndLine, EventCallback<string> OnOpenPath);

public abstract class FilePreviewViewer : ComponentBase
{
    [Parameter, EditorRequired] public FilePreviewViewContext Context { get; set; } = null!;
}
