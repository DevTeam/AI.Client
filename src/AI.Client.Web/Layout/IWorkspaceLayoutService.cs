namespace AI.Client.Web.Layout;

using Microsoft.AspNetCore.Components;

public interface IWorkspaceLayoutService : IAsyncDisposable
{
    Task AttachAsync(ElementReference workspace);

    Task ForwardContextMenuAsync(double clientX, double clientY, string backdropSelector);

    Task BlurActiveElementAsync();

    Task FocusAdjacentItemAsync(string containerSelector, string itemSelector, string key);
}
