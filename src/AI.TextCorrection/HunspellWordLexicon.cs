namespace AI.TextCorrection;

using WeCantSpell.Hunspell;
using System.Collections.Concurrent;

/// <summary>Embedded Hunspell dictionaries, with the application's technical vocabulary.</summary>
public sealed class HunspellWordLexicon : IWordLexicon, IWordLexiconPreparation, IWordSuggestions
{
    private readonly WordLexicon _applicationVocabulary;
    private readonly Dictionary<string, Lazy<WordList>> _dictionaries;
    private readonly Dictionary<string, ITextDictionaryResource> _sources;
    private readonly ConcurrentDictionary<string, WordList> _prepared = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task>> _preparing = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, char[]> _alphabets = new(StringComparer.Ordinal);
    private readonly IKeyboardLayouts _layouts;

    public HunspellWordLexicon(WordLexicon applicationVocabulary, ITextDictionaries dictionaries)
        : this(applicationVocabulary, dictionaries, new KeyboardLayouts()) { }

    public HunspellWordLexicon(WordLexicon applicationVocabulary, ITextDictionaries dictionaries, IKeyboardLayouts layouts)
    {
        _layouts = layouts;
        _applicationVocabulary = applicationVocabulary;
        _sources = dictionaries.All.ToDictionary(dictionary => dictionary.LanguageId, StringComparer.Ordinal);
        _dictionaries = dictionaries.All.ToDictionary(dictionary => dictionary.LanguageId,
            dictionary => new Lazy<WordList>(() =>
            {
                using var words = dictionary.OpenWords();
                using var affixes = dictionary.OpenAffixes();
                return WordList.CreateFromStreams(words, affixes);
            }), StringComparer.Ordinal);
    }

    public bool Contains(string layoutId, string word)
    {
        if (!_dictionaries.TryGetValue(layoutId, out var dictionary)) return false;
        if (_applicationVocabulary.Contains(layoutId, word)) return true;
        return (_prepared.TryGetValue(layoutId, out var prepared) ? prepared : dictionary.Value).Check(word);
    }

    public Task PrepareAsync(string languageId) => _preparing.GetOrAdd(languageId,
        id => new Lazy<Task>(() => PrepareDictionaryAsync(id))).Value;

    public IReadOnlyList<string> Suggest(string languageId, string word)
    {
        if (word.Length is < 4 or > 32 || !_dictionaries.TryGetValue(languageId, out var dictionary)) return [];
        var loaded = _prepared.TryGetValue(languageId, out var prepared) ? prepared : dictionary.Value;
        var alphabet = _alphabets.GetOrAdd(languageId, id => loaded.Affix.TryString
            .Concat(_layouts.All.Where(layout => layout.LanguageId == id).SelectMany(layout => layout.Keys + layout.ShiftKeys))
            .Where(char.IsLetter).Select(char.ToLowerInvariant).Distinct().ToArray());
        if (alphabet.Length == 0 || word.Any(character => !alphabet.Contains(char.ToLowerInvariant(character)))) return [];
        var source = word.ToLowerInvariant();
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Span<char> buffer = stackalloc char[source.Length + 1];
        var sentenceCase = char.IsUpper(word[0]);
        // Exhaustively check one-edit neighbours instead of Hunspell's expensive fuzzy/ngram search.
        // Two distinct matches already prove ambiguity; only matching words allocate strings.
        for (var index = 0; index < source.Length; index++)
        {
            source.AsSpan(0, index).CopyTo(buffer);
            source.AsSpan(index + 1).CopyTo(buffer[index..]);
            AddCandidate(loaded, buffer[..(source.Length - 1)], sentenceCase, candidates);
            if (candidates.Count > 1) return candidates.ToArray();
            if (index + 1 < source.Length)
            {
                source.AsSpan().CopyTo(buffer);
                (buffer[index], buffer[index + 1]) = (buffer[index + 1], buffer[index]);
                AddCandidate(loaded, buffer[..source.Length], sentenceCase, candidates);
                if (candidates.Count > 1) return candidates.ToArray();
            }
            foreach (var character in alphabet)
            {
                if (character == source[index]) continue;
                source.AsSpan().CopyTo(buffer);
                buffer[index] = character;
                AddCandidate(loaded, buffer[..source.Length], sentenceCase, candidates);
                if (candidates.Count > 1) return candidates.ToArray();
            }
        }
        for (var index = 0; index <= source.Length; index++)
        foreach (var character in alphabet)
        {
            source.AsSpan(0, index).CopyTo(buffer);
            buffer[index] = character;
            source.AsSpan(index).CopyTo(buffer[(index + 1)..]);
            AddCandidate(loaded, buffer, sentenceCase, candidates);
            if (candidates.Count > 1) return candidates.ToArray();
        }
        return candidates.ToArray();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Dictionary suggestion policies use instance methods by design.")]
    private void AddCandidate(WordList dictionary, Span<char> candidate, bool sentenceCase, HashSet<string> candidates)
    {
        if (sentenceCase) candidate[0] = char.ToUpperInvariant(candidate[0]);
        if (dictionary.Check(candidate)) candidates.Add(candidate.ToString());
    }

    private async Task PrepareDictionaryAsync(string languageId)
    {
        if (!_sources.TryGetValue(languageId, out var source)) return;
        if (_dictionaries[languageId].IsValueCreated)
        {
            _prepared.TryAdd(languageId, _dictionaries[languageId].Value);
            return;
        }
        using var words = new CooperativeReadStream(source.OpenWords());
        using var affixes = new CooperativeReadStream(source.OpenAffixes());
        var dictionary = await WordList.CreateFromStreamsAsync(words, affixes).ConfigureAwait(false);
        _prepared.TryAdd(languageId, dictionary);
    }

}
