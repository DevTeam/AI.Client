namespace AI.TextCorrection;

using System.Collections.Concurrent;

public sealed class TextCorrectionPreparation(IKeyboardLayouts layouts, IWordLexiconPreparation lexicon, IWordPlausibilityPreparation model)
    : ITextCorrectionPreparation
{
    private readonly ConcurrentDictionary<string, bool> _ready = new(StringComparer.Ordinal);

    public bool IsReady(IReadOnlyCollection<string> layoutIds) => Languages(layoutIds).All(_ready.ContainsKey);

    public async Task PrepareAsync(IReadOnlyCollection<string> layoutIds)
    {
        // Start after the caller has returned to the browser event loop.
        await Task.Delay(1).ConfigureAwait(false);
        foreach (var language in Languages(layoutIds))
        {
            if (_ready.ContainsKey(language)) continue;
            await lexicon.PrepareAsync(language).ConfigureAwait(false);
            await model.PrepareAsync(language).ConfigureAwait(false);
            _ready.TryAdd(language, true);
        }
    }

    private IEnumerable<string> Languages(IReadOnlyCollection<string> layoutIds) => layouts.All
        .Where(layout => layoutIds.Contains(layout.Id)).Select(layout => layout.LanguageId).Distinct(StringComparer.Ordinal);
}
