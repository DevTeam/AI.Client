namespace AI.Web.Pages;

using Microsoft.JSInterop;

public partial class Home
{
    [JSInvokable]
    public async Task<string> GetComposerAmbiguousSeparators() =>
        TextWordBoundaries.AmbiguousSeparators(await CorrectionLanguages.GetLayoutIdsAsync());

    [JSInvokable]
    public async Task<IReadOnlyList<AI.TextCorrection.TextReplacement>> AnalyzeComposerLayout(string text, AI.TextCorrection.TextRange[]? excludedRanges = null)
    {
        var layouts = await CorrectionLanguages.GetLayoutIdsAsync();
        if (layouts.Count == 0) return [];
        await TextCorrectionPreparation.PrepareAsync(layouts);
        // A language may have been deselected while its dictionary was loading.
        if (!layouts.Order().SequenceEqual((await CorrectionLanguages.GetLayoutIdsAsync()).Order())) return [];
        return TextCorrection.Analyze(text, layouts, excludedRanges);
    }

    // Settings and the shortcut beside the editor share the correction state.
    private AI.Web.Settings.TextCorrectionState _textCorrection = new([], true);

    private void OnTextCorrectionChanged() => _ = InvokeAsync(async () =>
    {
        _textCorrection = await CorrectionLanguages.GetStateAsync();
        StateHasChanged();
    });

    private async Task ToggleTextCorrectionAsync()
    {
        if (_textCorrection.Languages.Count == 0)
        {
            await OpenAppNavigationLinkAsync("aiclient://navigate/settings.chat.text_correction.languages", focus: true);
            return;
        }
        try
        {
            await CorrectionLanguages.SetEnabledAsync(!_textCorrection.Enabled);
            _textCorrection = await CorrectionLanguages.GetStateAsync();
            await PrepareTextCorrectionAsync();
        }
        catch (Exception error)
        {
            Notifications.ShowError($"Could not switch text correction: {error.Message}");
        }
    }

    private async Task PrepareTextCorrectionAsync()
    {
        try
        {
            var layouts = await CorrectionLanguages.GetLayoutIdsAsync();
            if (layouts.Count > 0) await TextCorrectionPreparation.PrepareAsync(layouts);
        }
        catch (Exception error)
        {
            await InvokeAsync(() => Notifications.ShowError($"Could not prepare text correction: {error.Message}"));
        }
    }

    private async Task<string?> PrepareComposerSubmissionAsync()
    {
        if (_composerHandle is not null)
            return await _composerHandle.InvokeAsync<string?>("prepareForSend");

        // Sending can happen before the DOM handle is attached.
        // Without input provenance, preserve the draft rather than correcting pasted text.
        return _chat.Message;
    }
}
