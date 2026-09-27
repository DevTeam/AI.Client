namespace AI.Application.Chat;

using System.Collections.Concurrent;
using Tools;

public sealed class ModelInstructionRegistry : IModelInstructionRegistry
{
    private readonly ConcurrentDictionary<Key, Entry> _entries = new();

    public IDisposable Begin(ToolRunContext run)
    {
        var key = Key.Of(run);
        _entries[key] = new Entry();
        return new Scope(() => _entries.TryRemove(key, out _));
    }

    public void Upsert(ToolRunContext run, ModelInstruction instruction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction.Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction.Content);
        if (_entries.TryGetValue(Key.Of(run), out var entry))
            entry.Instructions[instruction.Key] = instruction;
    }

    public void Remove(ToolRunContext run, string key)
    {
        if (_entries.TryGetValue(Key.Of(run), out var entry))
            entry.Instructions.TryRemove(key, out _);
    }

    public IReadOnlyList<ModelInstruction> List(ToolRunContext run) =>
        _entries.TryGetValue(Key.Of(run), out var entry)
            ? entry.Instructions.Values.ToArray()
            : [];

    public void Acknowledge(ToolRunContext run, IReadOnlyCollection<string> keys)
    {
        if (!_entries.TryGetValue(Key.Of(run), out var entry)) return;
        foreach (var key in keys)
            if (entry.Instructions.TryGetValue(key, out var instruction)
                && instruction.Lifetime is ModelInstructionLifetime.Request or ModelInstructionLifetime.UntilAcknowledged)
                entry.Instructions.TryRemove(key, out _);
    }

    private readonly record struct Key(Guid ProjectId, Guid ChatId, Guid BranchId)
    {
        public static Key Of(ToolRunContext run) => new(run.ProjectId, run.ChatId, run.BranchId);
    }

    private sealed class Entry
    {
        public ConcurrentDictionary<string, ModelInstruction> Instructions { get; } = new(StringComparer.Ordinal);
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
