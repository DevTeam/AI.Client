namespace AI.Web.State;

using Microsoft.JSInterop;
using System.Text.Json;

public sealed class ChatTipsState(IJSRuntime jsRuntime) : IChatTipsState
{
    private const string StorageKey = "ai-client.chat-tips.v1";

    private sealed record Saved(bool Hidden, string[]? Used);

    private readonly HashSet<string> _used = new(StringComparer.Ordinal);
    private Task? _initialization;

    public bool IsHidden { get; private set; }

    public event Action? Changed;

    public bool IsUsed(string tipId) => _used.Contains(tipId);

    // Shared by every caller: the tips and the page both ask for the state on first render, and
    // the second must wait for the same read rather than see an empty one.
    public Task InitializeAsync() => _initialization ??= LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            var json = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (string.IsNullOrEmpty(json) || JsonSerializer.Deserialize<Saved>(json) is not { } saved) return;
            IsHidden = saved.Hidden;
            _used.UnionWith(saved.Used ?? []);
        }
        catch (Exception error) when (error is JSException or JsonException)
        {
            // Blocked or corrupt storage: the tips simply start fresh.
        }
    }

    public async Task SetHiddenAsync(bool hidden)
    {
        await InitializeAsync();
        if (IsHidden == hidden) return;
        IsHidden = hidden;
        await SaveAsync();
    }

    public async Task MarkUsedAsync(string tipId)
    {
        await InitializeAsync();
        if (!_used.Add(tipId)) return;
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        Changed?.Invoke();
        try
        {
            await jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey,
                JsonSerializer.Serialize(new Saved(IsHidden, [.. _used.Order(StringComparer.Ordinal)])));
        }
        catch (JSException)
        {
        }
    }
}
