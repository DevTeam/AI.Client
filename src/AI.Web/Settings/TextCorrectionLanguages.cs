namespace AI.Web.Settings;

using AI.TextCorrection;

public interface ITextCorrectionLanguages
{
    IReadOnlyList<KeyboardLayout> Available { get; }
    ValueTask<IReadOnlyCollection<string>> GetLayoutIdsAsync();
    ValueTask SetLanguageAsync(string languageId, bool enabled);
}

public sealed class TextCorrectionLanguages(IClientSettingsService settings, IKeyboardLayouts layouts,
    ISupportedCorrectionLayouts supported) : ITextCorrectionLanguages
{
    public IReadOnlyList<KeyboardLayout> Available { get; } = layouts.All
        .Where(layout => supported.Ids.Contains(layout.Id)).DistinctBy(layout => layout.LanguageId).ToArray();

    public async ValueTask<IReadOnlyCollection<string>> GetLayoutIdsAsync()
    {
        var selected = (await settings.GetAsync()).TextCorrectionLanguages ?? [];
        return layouts.All.Where(layout => supported.Ids.Contains(layout.Id)
            && selected.Contains(layout.LanguageId))
            .Select(layout => layout.Id).ToArray();
    }

    public async ValueTask SetLanguageAsync(string languageId, bool enabled)
    {
        if (!Available.Any(layout => layout.LanguageId == languageId))
            throw new ArgumentException("Unsupported correction language.", nameof(languageId));
        await settings.UpdateAsync(current =>
        {
            var selected = current.TextCorrectionLanguages ?? [];
            return current with
            {
                TextCorrectionLanguages = enabled ? selected.Append(languageId).Distinct(StringComparer.Ordinal).ToArray()
                    : selected.Where(id => id != languageId).ToArray()
            };
        });
    }
}
