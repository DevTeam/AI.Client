namespace AI.Web.Layout;

using Microsoft.AspNetCore.Components;

public interface IWorkspaceLayoutService : IAsyncDisposable
{
    Task AttachAsync(ElementReference workspace);

    Task ForwardContextMenuAsync(double clientX, double clientY, string backdropSelector);

    Task BlurActiveElementAsync();

    Task FocusAdjacentItemAsync(string containerSelector, string itemSelector, string key);

    /// <summary>Scrolls the element with <paramref name="elementId"/> into its scroll box, as little as possible.</summary>
    Task RevealElementAsync(string elementId);
}
