namespace AI.Web.Layout;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public interface IWorkspaceLayoutService : IAsyncDisposable
{
    /// <summary>
    /// Attaches the side-panel layout to <paramref name="workspace"/>. The chat widget column is the
    /// page's to render, so the layout asks <paramref name="callbacks"/> to open or close it through
    /// its <c>SetChatWidgetsOpen(bool)</c> method. The panels start as <paramref name="panels"/>
    /// describes, and each change is handed to <c>SaveWorkspacePanels(WorkspacePanels)</c>.
    /// </summary>
    Task AttachAsync<TCallbacks>(ElementReference workspace, DotNetObjectReference<TCallbacks> callbacks, WorkspacePanels? panels)
        where TCallbacks : class;

    Task ForwardContextMenuAsync(double clientX, double clientY, string backdropSelector);

    Task BlurActiveElementAsync();

    Task FocusAdjacentItemAsync(string containerSelector, string itemSelector, string key);

    /// <summary>Scrolls the element with <paramref name="elementId"/> into its scroll box, as little as possible.</summary>
    Task RevealElementAsync(string elementId);
}
