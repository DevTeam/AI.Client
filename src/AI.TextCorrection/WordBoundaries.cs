namespace AI.TextCorrection;

using System.Collections.Concurrent;

public interface IWordBoundaries
{
    string AmbiguousSeparators(IReadOnlyCollection<string> layoutIds);
}

public sealed class WordBoundaries(IKeyboardLayouts layouts, ILayoutConverter converter) : IWordBoundaries
{
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

    public string AmbiguousSeparators(IReadOnlyCollection<string> layoutIds)
    {
        var active = layouts.All.Where(layout => layoutIds.Contains(layout.Id)).ToArray();
        var key = string.Join('\0', active.Select(layout => layout.Id));
        return _cache.TryGetValue(key, out var separators) ? separators : _cache.GetOrAdd(key, Build(active));
    }

    private string Build(KeyboardLayout[] active)
    {
        var ambiguous = new HashSet<char>();
        foreach (var source in active)
        foreach (var character in source.Keys.Concat(source.ShiftKeys).Distinct())
        {
            if (!char.IsPunctuation(character) && !char.IsSymbol(character)) continue;
            if (active.Any(target => target.Id != source.Id && char.IsLetter(converter.Convert(character, source, target))))
                ambiguous.Add(character);
        }
        return new string(ambiguous.Order().ToArray());
    }
}
