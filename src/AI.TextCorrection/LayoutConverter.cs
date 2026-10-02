namespace AI.TextCorrection;

using System.Collections.Concurrent;
using System.Collections.Frozen;

public sealed class LayoutConverter : ILayoutConverter
{
    private readonly ConcurrentDictionary<(KeyboardLayout Source, KeyboardLayout Target), FrozenDictionary<char, char>> _maps = new();
    private readonly Func<(KeyboardLayout Source, KeyboardLayout Target), FrozenDictionary<char, char>> _mapFactory;

    public LayoutConverter() => _mapFactory = BuildMap;

    public string Convert(string text, KeyboardLayout source, KeyboardLayout target)
    {
        var map = _maps.GetOrAdd((source, target), _mapFactory);
        return string.Create(text.Length, (text, map), (buffer, state) =>
        {
            for (var index = 0; index < buffer.Length; index++)
                buffer[index] = state.map.GetValueOrDefault(state.text[index], state.text[index]);
        });
    }

    public char Convert(char character, KeyboardLayout source, KeyboardLayout target) =>
        _maps.GetOrAdd((source, target), _mapFactory).GetValueOrDefault(character, character);

    private FrozenDictionary<char, char> BuildMap((KeyboardLayout Source, KeyboardLayout Target) pair)
    {
        var map = new Dictionary<char, char>();
        for (var index = 0; index < pair.Source.Keys.Length; index++)
            map.TryAdd(pair.Source.Keys[index], pair.Target.Keys[index]);
        for (var index = 0; index < pair.Source.ShiftKeys.Length; index++)
            map.TryAdd(pair.Source.ShiftKeys[index], pair.Target.ShiftKeys[index]);
        return map.ToFrozenDictionary();
    }
}
