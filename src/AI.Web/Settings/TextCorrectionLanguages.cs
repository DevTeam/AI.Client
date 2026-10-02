namespace AI.Web.Settings;

using AI.TextCorrection;

/// <summary>What the person chose: the correction languages, and whether correction is switched on for now.</summary>
public sealed record TextCorrectionState(IReadOnlyList<KeyboardLayout> Languages, bool Enabled)
{
    /// <summary>Whether the composer corrects what is typed: at least one language and not paused.</summary>
    public bool IsActive => Enabled && Languages.Count > 0;
}

public interface ITextCorrectionLanguages
{
    IReadOnlyList<KeyboardLayout> Available { get; }

    /// <summary>Raised after the languages or the on/off switch change, wherever they were changed.</summary>
    event Action? Changed;

    ValueTask<TextCorrectionState> GetStateAsync();

    /// <summary>The layouts the composer corrects with right now: none while correction is switched off.</summary>
    ValueTask<IReadOnlyCollection<string>> GetLayoutIdsAsync();

    ValueTask SetLanguageAsync(string languageId, bool enabled);

    /// <summary>Pauses or resumes correction without forgetting the selected languages.</summary>
    ValueTask SetEnabledAsync(bool enabled);
}

public sealed class TextCorrectionLanguages(IClientSettingsService settings, IKeyboardLayouts layouts,
    ISupportedCorrectionLayouts supported) : ITextCorrectionLanguages
{
    public IReadOnlyList<KeyboardLayout> Available { get; } = layouts.All
        .Where(layout => supported.Ids.Contains(layout.Id)).DistinctBy(layout => layout.LanguageId).ToArray();

    public event Action? Changed;

    public async ValueTask<TextCorrectionState> GetStateAsync()
    {
        var current = await settings.GetAsync();
        var selected = current.TextCorrectionLanguages ?? [];
        return new TextCorrectionState(Available.Where(layout => selected.Contains(layout.LanguageId)).ToArray(), current.TextCorrectionEnabled);
    }

    public async ValueTask<IReadOnlyCollection<string>> GetLayoutIdsAsync()
    {
        var current = await settings.GetAsync();
        if (!current.TextCorrectionEnabled) return [];
        var selected = current.TextCorrectionLanguages ?? [];
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
        Changed?.Invoke();
    }

    public async ValueTask SetEnabledAsync(bool enabled)
    {
        await settings.UpdateAsync(current => current with { TextCorrectionEnabled = enabled });
        Changed?.Invoke();
    }
}
