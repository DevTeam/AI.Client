namespace AI.TextCorrection;

public interface ISupportedCorrectionLayouts
{
    IReadOnlyCollection<string> Ids { get; }
}

public sealed class SupportedCorrectionLayouts(IKeyboardLayouts layouts, ITextDictionaries dictionaries)
    : ISupportedCorrectionLayouts
{
    public IReadOnlyCollection<string> Ids { get; } = layouts.All
        .Where(layout => dictionaries.All.Any(dictionary => dictionary.LanguageId == layout.LanguageId))
        .Select(layout => layout.Id).Distinct(StringComparer.Ordinal).ToArray();
}
