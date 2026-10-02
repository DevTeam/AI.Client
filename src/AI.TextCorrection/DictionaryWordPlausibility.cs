namespace AI.TextCorrection;

using System.Text;
using System.Collections.Concurrent;
using AI.TextCorrection.Resources;

/// <summary>Compares packed letter triples from build-time indexes, without language-specific rules.</summary>
public sealed class DictionaryWordPlausibility : IWordPlausibility, IWordPlausibilityPreparation
{
    private readonly Dictionary<string, Lazy<ulong[]>> _models;
    private readonly Encoding _encoding = new UTF8Encoding(false, true);
    private readonly Dictionary<string, ITextDictionaryResource> _sources;
    private readonly ConcurrentDictionary<string, ulong[]> _prepared = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task>> _preparing = new(StringComparer.Ordinal);
    private readonly ITrigramIndexFormat _format;

    public DictionaryWordPlausibility(ITextDictionaries dictionaries) : this(dictionaries, new TrigramIndexFormat()) { }

    public DictionaryWordPlausibility(ITextDictionaries dictionaries, ITrigramIndexFormat format)
    {
        _format = format;
        _sources = dictionaries.All.ToDictionary(dictionary => dictionary.LanguageId, StringComparer.Ordinal);
        _models = dictionaries.All.ToDictionary(dictionary => dictionary.LanguageId,
            dictionary => new Lazy<ulong[]>(() => Read(dictionary)), StringComparer.Ordinal);
    }

    public double Score(string layoutId, string word)
    {
        if (word.Length < 3 || !_models.TryGetValue(layoutId, out var model)) return 0;
        var triples = _prepared.TryGetValue(layoutId, out var prepared) ? prepared : model.Value;
        var found = 0;
        var letters = word.AsSpan();
        for (var index = 0; index + 2 < letters.Length; index++)
            if (triples.AsSpan().BinarySearch(_format.Encode(letters[index], letters[index + 1], letters[index + 2])) >= 0) found++;
        return (double)found / (letters.Length - 2);
    }

    public Task PrepareAsync(string languageId) => _preparing.GetOrAdd(languageId,
        id => new Lazy<Task>(() => PrepareModelAsync(id))).Value;

    private async Task PrepareModelAsync(string languageId)
    {
        if (!_sources.TryGetValue(languageId, out var source)) return;
        if (_models[languageId].IsValueCreated || source is IPreparedTextDictionaryResource)
        {
            _prepared.TryAdd(languageId, _models[languageId].Value);
            return;
        }
        // Alternative dictionary providers retain cooperative preparation from their word lists.
        using var stream = new CooperativeReadStream(source.OpenWords());
        using var reader = new StreamReader(stream, _encoding);
        var triples = new HashSet<ulong>();
        var lines = 0;
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            _format.AddWord(line.AsSpan(), triples);
            if (++lines % 512 == 0) await Task.Delay(1).ConfigureAwait(false);
        }
        _prepared.TryAdd(languageId, triples.Order().ToArray());
    }

    private ulong[] Read(ITextDictionaryResource dictionary)
    {
        if (dictionary is IPreparedTextDictionaryResource prepared)
        {
            using var index = prepared.OpenTrigrams();
            return _format.Read(index);
        }
        using var stream = dictionary.OpenWords();
        using var reader = new StreamReader(stream, _encoding);
        var triples = new HashSet<ulong>();
        while (reader.ReadLine() is { } line) _format.AddWord(line.AsSpan(), triples);
        return triples.Order().ToArray();
    }
}
