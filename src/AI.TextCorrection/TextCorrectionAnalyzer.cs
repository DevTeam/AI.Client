namespace AI.TextCorrection;

using System.Collections.Concurrent;

public sealed class TextCorrectionAnalyzer(IKeyboardLayouts layouts, ILayoutConverter converter, IWordLexicon lexicon, IWordPlausibility plausibility, IWordBoundaries boundaries, ITextTokenizer tokenizer)
    : ITextCorrectionAnalyzer
{
    private readonly ConcurrentDictionary<string, string> _changedSeparators = new(StringComparer.Ordinal);
    private readonly string _sentenceBreaks = ".!?\n";

    public TextCorrectionAnalyzer(IKeyboardLayouts layouts, ILayoutConverter converter, IWordLexicon lexicon, IWordPlausibility plausibility, IWordBoundaries boundaries)
        : this(layouts, converter, lexicon, plausibility, boundaries, new TextTokenizer()) { }

    public IReadOnlyList<TextReplacement> Analyze(string text, IReadOnlyCollection<string> layoutIds)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(layoutIds);
        if (text.Length > 8192) return [];
        var active = layouts.All.Where(layout => layoutIds.Contains(layout.Id)).ToArray();
        if (active.Length < 2) return [];
        var ambiguous = boundaries.AmbiguousSeparators(layoutIds);
        var key = string.Join('\0', active.Select(layout => layout.Id));
        if (!_changedSeparators.TryGetValue(key, out var changed))
            changed = _changedSeparators.GetOrAdd(key, ChangedSeparators(active, ambiguous));
        var protectedRanges = tokenizer.ProtectedRanges(text);
        var tokens = tokenizer.Tokenize(text, ambiguous, changed, protectedRanges);
        var words = new Dictionary<(string Layout, string Word), bool>();
        bool Known(string id, string word)
        {
            var key = (id, word);
            if (!words.TryGetValue(key, out var known)) words.Add(key, known = lexicon.Contains(id, word));
            return known;
        }

        var candidates = new List<TextReplacement>();
        var uncertain = new List<TextReplacement>();
        foreach (var token in tokens)
        {
            var word = token.Value.TrimEnd('!', '?', '.', ',');
            if (word.Length == 0 || word.All(char.IsDigit)) continue;
            // Single letters need context: Hunspell recognizes "d", but here it may mean "в".
            if (word.Length > 1 && active.Any(layout => Known(layout.LanguageId, word))) continue;
            if (word.Skip(1).Any(char.IsUpper) && !word.All(char.IsUpper)) continue;
            var alternatives = new List<TextReplacement>();
            foreach (var source in active)
            {
                foreach (var target in active)
                {
                    if (source.Id == target.Id) continue;
                    // The user may switch layouts halfway through a word. Keep letters
                    // already in the target alphabet; convert the remaining keystrokes.
                    if (!word.All(character => source.Keys.Contains(character) || source.ShiftKeys.Contains(character)
                        || char.IsLetter(character) && (target.Keys.Contains(character) || target.ShiftKeys.Contains(character)))) continue;
                    var converted = converter.Convert(word, source, target);
                    var convertedWord = converted.TrimEnd('!', '?', '.', ',');
                    if (converted == word || convertedWord.Length == 0 || convertedWord.Any(character => !char.IsLetter(character))) continue;
                    if (Known(target.LanguageId, convertedWord))
                        alternatives.Add(new TextReplacement(token.Index, word.Length, converted, target.Id, word.Length >= 5 ? 0.98 : 0.9));
                    else if (word.Length is >= 3 and <= 24 && word.All(char.IsLower)
                        && !Known(source.LanguageId, word))
                        uncertain.Add(new TextReplacement(token.Index, word.Length, converted, target.Id, 0.85));
                }
            }
            if (alternatives.Select(candidate => candidate.Text).Distinct(StringComparer.Ordinal).Count() == 1)
                candidates.Add(alternatives[0]);
        }

        // Evidence comes only from known words, never from speculative conversions.
        var anchors = new List<TextReplacement>(candidates.Where(candidate => candidate.Length >= 3));
        foreach (var token in tokens)
        {
            var word = token.Value.TrimEnd('!', '?', '.', ',');
            if (word.Length < 3 || !word.All(char.IsLetter)) continue;
            foreach (var layout in active)
                if (word.All(character => layout.Keys.Contains(character) || layout.ShiftKeys.Contains(character)) && Known(layout.LanguageId, word))
                    anchors.Add(new TextReplacement(token.Index, word.Length, word, layout.Id, 1));
        }

        bool HasContext(TextReplacement candidate, bool strong)
        {
            var nearby = anchors.Where(anchor => anchor.Start != candidate.Start && anchor.LayoutId == candidate.LayoutId
                && Math.Abs(anchor.Start - candidate.Start) < 60 && SamePhrase(text, candidate, anchor, protectedRanges)
                && !tokens.Any(token => token.Index > Math.Min(anchor.Start, candidate.Start)
                    && token.Index < Math.Max(anchor.Start, candidate.Start) && token.Length > 1
                    && active.Any(layout => layout.Id != candidate.LayoutId && Known(layout.LanguageId, token.Value.TrimEnd('!', '?', '.', ',')))))
                .DistinctBy(anchor => anchor.Start).ToArray();
            return strong ? nearby.Length >= 2 && nearby.Any(anchor => anchor.Length >= 5)
                : nearby.Any(anchor => anchor.Length >= 4) || nearby.Count(anchor => anchor.Length >= 3) >= 2;
        }

        return candidates.Where(candidate => candidate.Score >= 0.98 || HasContext(candidate, false))
            .Concat(uncertain.Where(candidate => HasContext(candidate, true)
                && plausibility.Score(active.First(layout => layout.Id == candidate.LayoutId).LanguageId, candidate.Text.TrimEnd('!', '?', '.', ',')) >= 0.5
                && plausibility.Score(active.First(layout => layout.Id == candidate.LayoutId).LanguageId, candidate.Text.TrimEnd('!', '?', '.', ','))
                    >= active.Where(layout => layout.Id != candidate.LayoutId)
                        .Max(layout => plausibility.Score(layout.LanguageId, text.Substring(candidate.Start, candidate.Length))) + 0.25)
                .GroupBy(candidate => candidate.Start).Where(group => group.Count() == 1).Select(group => group.Single()))
            .OrderBy(candidate => candidate.Start).ToArray();
    }

    private bool SamePhrase(string text, TextReplacement candidate, TextReplacement anchor, TextRange[] protectedRanges)
    {
        var start = Math.Min(candidate.Start + candidate.Length, anchor.Start + anchor.Length);
        var end = Math.Max(candidate.Start, anchor.Start);
        if (end <= start) return true;
        return text.AsSpan(start, end - start).IndexOfAny(_sentenceBreaks) < 0
            && !protectedRanges.Any(range => start < range.Index + range.Length && end > range.Index);
    }

    private string ChangedSeparators(KeyboardLayout[] active, string ambiguous)
    {
        var changed = new HashSet<char>();
        foreach (var source in active)
        foreach (var character in source.Keys.Concat(source.ShiftKeys))
            if ((char.IsPunctuation(character) || char.IsSymbol(character)) && !ambiguous.Contains(character)
                && active.Any(target => converter.Convert(character, source, target) != character)) changed.Add(character);
        return new string(changed.Order().ToArray());
    }

}
