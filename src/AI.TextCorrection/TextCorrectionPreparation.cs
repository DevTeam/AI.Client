namespace AI.TextCorrection;

using System.Collections.Concurrent;

public sealed class TextCorrectionPreparation(IKeyboardLayouts layouts, IWordLexiconPreparation lexicon, IWordPlausibilityPreparation model)
    : ITextCorrectionPreparation
{
    private readonly ConcurrentDictionary<string, bool> _ready = new(StringComparer.Ordinal);

    public bool IsReady(IReadOnlyCollection<string> layoutIds)
    {
        var languages = Languages(layoutIds).ToArray();
        return languages.All(language => _ready.TryGetValue(language, out var modelReady) && (languages.Length < 2 || modelReady));
    }

    public async Task PrepareAsync(IReadOnlyCollection<string> layoutIds)
    {
        // Start after the caller has returned to the browser event loop.
        await Task.Delay(1).ConfigureAwait(false);
        var languages = Languages(layoutIds).ToArray();
        var withModels = languages.Length >= 2;
        foreach (var language in languages)
        {
            if (_ready.TryGetValue(language, out var modelReady) && (!withModels || modelReady)) continue;
            await lexicon.PrepareAsync(language).ConfigureAwait(false);
            if (withModels) await model.PrepareAsync(language).ConfigureAwait(false);
            _ready.AddOrUpdate(language, withModels, (_, ready) => ready || withModels);
        }
    }

    private IEnumerable<string> Languages(IReadOnlyCollection<string> layoutIds) => layouts.All
        .Where(layout => layoutIds.Contains(layout.Id)).Select(layout => layout.LanguageId).Distinct(StringComparer.Ordinal);
}
