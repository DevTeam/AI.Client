namespace AI.Web.Layout;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public sealed class WorkspaceLayoutService(IJSRuntime jsRuntime) : IWorkspaceLayoutService
{
    private IJSObjectReference? _module;
    private IJSObjectReference? _handle;

    public async Task AttachAsync(ElementReference workspace)
    {
        _module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/workspaceLayout.js");
        _handle = await _module.InvokeAsync<IJSObjectReference>("attach", workspace);
    }

    public Task ForwardContextMenuAsync(double clientX, double clientY, string backdropSelector) =>
        _module is null ? Task.CompletedTask : _module.InvokeVoidAsync("forwardContextMenu", clientX, clientY, backdropSelector).AsTask();

    public Task BlurActiveElementAsync() =>
        _module is null ? Task.CompletedTask : _module.InvokeVoidAsync("blurActiveElement").AsTask();

    public Task FocusAdjacentItemAsync(string containerSelector, string itemSelector, string key) =>
        _module is null ? Task.CompletedTask : _module.InvokeVoidAsync("focusAdjacentItem", containerSelector, itemSelector, key).AsTask();

    public async ValueTask DisposeAsync()
    {
        if (_handle is not null)
        {
            await _handle.InvokeVoidAsync("dispose");
            await _handle.DisposeAsync();
        }

        if (_module is not null)
        {
            await _module.DisposeAsync();
        }
    }
}
