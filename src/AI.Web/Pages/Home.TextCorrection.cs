namespace AI.Web.Pages;

using Microsoft.JSInterop;

public partial class Home
{
    [JSInvokable]
    public async Task<string> GetComposerAmbiguousSeparators() =>
        TextWordBoundaries.AmbiguousSeparators(await CorrectionLanguages.GetLayoutIdsAsync());

    [JSInvokable]
    public async Task<IReadOnlyList<AI.TextCorrection.TextReplacement>> AnalyzeComposerLayout(string text)
    {
        var layouts = await CorrectionLanguages.GetLayoutIdsAsync();
        if (layouts.Count < 2) return [];
        await TextCorrectionPreparation.PrepareAsync(layouts);
        // A language may have been deselected while its dictionary was loading.
        if (!layouts.Order().SequenceEqual((await CorrectionLanguages.GetLayoutIdsAsync()).Order())) return [];
        return TextCorrection.Analyze(text, layouts);
    }

    private async Task PrepareTextCorrectionAsync()
    {
        try
        {
            var layouts = await CorrectionLanguages.GetLayoutIdsAsync();
            if (layouts.Count >= 2) await TextCorrectionPreparation.PrepareAsync(layouts);
        }
        catch (Exception error)
        {
            await InvokeAsync(() => Notifications.ShowError($"Could not prepare keyboard layout correction: {error.Message}"));
        }
    }

    private async Task<string?> PrepareComposerSubmissionAsync()
    {
        if (_composerHandle is not null)
            return await _composerHandle.InvokeAsync<string?>("prepareForSend");

        // Sending can happen before the DOM handle is attached.
        var original = _chat.Message;
        var edits = await AnalyzeComposerLayout(original);
        if (_chat.Message != original) return null;
        var corrected = original;
        foreach (var edit in edits.OrderByDescending(edit => edit.Start))
            corrected = corrected[..edit.Start] + edit.Text + corrected[(edit.Start + edit.Length)..];
        return corrected;
    }
}
