namespace AI.TextCorrection;

using System.Collections.Concurrent;

public interface IWordSuggestions
{
    IReadOnlyList<string> Suggest(string languageId, string word);
}

public sealed record SpellingReplacement(string Text, string LanguageId);

public interface ISpellingCorrection
{
    SpellingReplacement? Correct(string word, IReadOnlyCollection<string> languageIds);
}

/// <summary>Accepts one unambiguous dictionary suggestion one edit away; scores are not probabilities.</summary>
public sealed class SpellingCorrection(IWordLexicon lexicon, IWordSuggestions suggestions) : ISpellingCorrection
{
    private readonly ConcurrentDictionary<(string Languages, string Word), Lazy<SpellingReplacement?>> _cache = new();

    public SpellingReplacement? Correct(string word, IReadOnlyCollection<string> languageIds)
    {
        // Short words, acronyms and mixed-case identifiers are especially ambiguous.
        if (word.Length is < 4 or > 32 || !word.All(char.IsLetter) || word.Skip(1).Any(char.IsUpper)) return null;
        var languages = languageIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (languages.Length == 0 || languages.Any(language => lexicon.Contains(language, word))) return null;
        var key = (string.Join('\0', languages), word);
        if (_cache.TryGetValue(key, out var cached)) return cached.Value;
        if (_cache.Count >= 2048) _cache.Clear();
        return _cache.GetOrAdd(key, new Lazy<SpellingReplacement?>(() => Find(word, languages))).Value;
    }

    private SpellingReplacement? Find(string word, string[] languages)
    {
        SpellingReplacement? result = null;
        foreach (var language in languages)
        foreach (var suggestion in suggestions.Suggest(language, word))
        {
            if (suggestion.Length == 0 || !suggestion.All(char.IsLetter) || suggestion.Skip(1).Any(char.IsUpper)
                || char.IsLower(word[0]) && char.IsUpper(suggestion[0])) continue;
            var candidate = char.IsUpper(word[0])
                ? char.ToUpperInvariant(suggestion[0]) + suggestion[1..] : suggestion.ToLowerInvariant();
            if (!OneEditAway(word.AsSpan(), candidate.AsSpan()) || !lexicon.Contains(language, candidate)) continue;
            if (result is not null && !string.Equals(result.Text, candidate, StringComparison.Ordinal)) return null;
            result ??= new SpellingReplacement(candidate, language);
        }
        return result;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Spelling policies use instance methods by design.")]
    private bool OneEditAway(ReadOnlySpan<char> word, ReadOnlySpan<char> candidate)
    {
        if (Math.Abs(word.Length - candidate.Length) > 1) return false;
        var index = 0;
        while (index < Math.Min(word.Length, candidate.Length)
            && char.ToLowerInvariant(word[index]) == char.ToLowerInvariant(candidate[index])) index++;
        if (index == Math.Min(word.Length, candidate.Length)) return word.Length != candidate.Length;
        if (word.Length > candidate.Length)
            return word[(index + 1)..].Equals(candidate[index..], StringComparison.OrdinalIgnoreCase);
        if (candidate.Length > word.Length)
            return word[index..].Equals(candidate[(index + 1)..], StringComparison.OrdinalIgnoreCase);
        if (word[(index + 1)..].Equals(candidate[(index + 1)..], StringComparison.OrdinalIgnoreCase)) return true;
        return index + 1 < word.Length
            && char.ToLowerInvariant(word[index]) == char.ToLowerInvariant(candidate[index + 1])
            && char.ToLowerInvariant(word[index + 1]) == char.ToLowerInvariant(candidate[index])
            && word[(index + 2)..].Equals(candidate[(index + 2)..], StringComparison.OrdinalIgnoreCase);
    }
}
