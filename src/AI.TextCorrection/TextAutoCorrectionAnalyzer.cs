namespace AI.TextCorrection;

public interface ITextAutoCorrectionAnalyzer
{
    IReadOnlyList<TextReplacement> Analyze(string text, IReadOnlyCollection<string> layoutIds, IReadOnlyList<TextRange>? excludedRanges = null);
}

/// <summary>Combines layout and spelling edits in the original text's coordinates.</summary>
public sealed class TextAutoCorrectionAnalyzer(ITextCorrectionAnalyzer layoutsAnalyzer, ISpellingCorrection spelling,
    IKeyboardLayouts layouts, ITextTokenizer tokenizer) : ITextAutoCorrectionAnalyzer
{
    public IReadOnlyList<TextReplacement> Analyze(string text, IReadOnlyCollection<string> layoutIds, IReadOnlyList<TextRange>? excludedRanges = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(layoutIds);
        if (text.Length > 8192) return [];
        var active = layouts.All.Where(layout => layoutIds.Contains(layout.Id)).ToArray();
        if (active.Length == 0) return [];
        var protectedRanges = tokenizer.ProtectedRanges(text);
        // Newlines prevent context crossing an excluded fragment. Preserve code protection
        // even when a pasted fragment contains the opening quote or code fence.
        var input = excludedRanges is { Count: > 0 } ? string.Create(text.Length,
            (text, ranges: excludedRanges.Concat(protectedRanges)), (span, state) =>
        {
            state.text.AsSpan().CopyTo(span);
            foreach (var range in state.ranges)
                if (range.Index >= 0 && range.Length > 0 && range.Index <= span.Length - range.Length)
                    span.Slice(range.Index, range.Length).Fill('\n');
        }) : text;
        var languages = active.Select(layout => layout.LanguageId).Distinct(StringComparer.Ordinal).ToArray();
        var edits = new List<TextReplacement>();
        if (languages.Length >= 2)
            foreach (var edit in layoutsAnalyzer.Analyze(input, layoutIds))
            {
                var language = active.First(layout => layout.Id == edit.LayoutId).LanguageId;
                var word = edit.Text.TrimEnd('!', '?', '.', ',');
                var correction = spelling.Correct(word, [language]);
                edits.Add(correction is null ? edit : edit with { Text = correction.Text + edit.Text[word.Length..] });
            }

        foreach (var token in tokenizer.Tokenize(input, "", "", protectedRanges))
        {
            if (edits.Any(edit => token.Index < edit.Start + edit.Length && token.Index + token.Length > edit.Start)) continue;
            // Preserve paths, identifiers, email addresses and file names as whole chunks.
            if (IsTechnicalChunk(text, token)) continue;
            var correction = spelling.Correct(token.Value, languages);
            if (correction is not null)
                edits.Add(new TextReplacement(token.Index, token.Length, correction.Text,
                    active.First(layout => layout.LanguageId == correction.LanguageId).Id, 0.95));
        }
        return edits.OrderBy(edit => edit.Start).ToArray();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Token protection policies use instance methods by design.")]
    private bool IsTechnicalChunk(string text, TextToken token)
    {
        var start = token.Index;
        var end = start + token.Length;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        var chunk = text.AsSpan(start, end - start);
        if (chunk.IndexOfAny("/\\@_#=+".AsSpan()) >= 0) return true;
        for (var index = 0; index < chunk.Length; index++)
        {
            if (char.IsDigit(chunk[index])) return true;
            if (index > 0 && index + 1 < chunk.Length && chunk[index] is '.' or ':' or '-'
                && char.IsLetterOrDigit(chunk[index - 1]) && char.IsLetterOrDigit(chunk[index + 1])) return true;
        }
        return false;
    }
}
