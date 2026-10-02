namespace AI.TextCorrection;

using WeCantSpell.Hunspell;
using System.Collections.Concurrent;

/// <summary>Embedded Hunspell dictionaries, with the application's technical vocabulary.</summary>
public sealed class HunspellWordLexicon : IWordLexicon, IWordLexiconPreparation
{
    private readonly WordLexicon _applicationVocabulary;
    private readonly Dictionary<string, Lazy<WordList>> _dictionaries;
    private readonly Dictionary<string, ITextDictionaryResource> _sources;
    private readonly ConcurrentDictionary<string, WordList> _prepared = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task>> _preparing = new(StringComparer.Ordinal);

    public HunspellWordLexicon(WordLexicon applicationVocabulary, ITextDictionaries dictionaries)
    {
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
