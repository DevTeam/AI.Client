namespace AI.Web.Tests.Settings;

using AI.TextCorrection;
using AI.Web.Settings;
using Shouldly;
using Xunit;

public sealed class TextCorrectionLanguagesTests
{
    private readonly TextCorrectionComposition _composition = new();
    private readonly Settings _settings = new();

    [Fact]
    public async Task DefaultsToNoSelectedLanguages()
    {
        var selection = CreateSelection();
        (await selection.GetLayoutIdsAsync()).ShouldBeEmpty();
        selection.Available.Select(layout => layout.LanguageId).ShouldBe(["en", "ru", "fr", "es"]);
    }

    [Fact]
    public async Task SavesSelectionAndAppliesItToSubsequentAnalysisRequests()
    {
        var selection = CreateSelection();
        await selection.SetLanguageAsync("en", true);
        (await selection.GetLayoutIdsAsync()).ShouldBe(["en"]);
        await selection.SetLanguageAsync("ru", true);
        (await selection.GetLayoutIdsAsync()).ShouldBe(["en", "ru"]);
        (await new TextCorrectionLanguages(_settings, _composition.Layouts, _composition.SupportedLayouts)
            .GetLayoutIdsAsync()).ShouldBe(["en", "ru"]);
        await selection.SetLanguageAsync("fr", true);
        await selection.SetLanguageAsync("fr", true);
        (await selection.GetLayoutIdsAsync()).ShouldBe(["en", "ru", "fr"]);
        _settings.Current.TextCorrectionLanguages!.Count(id => id == "fr").ShouldBe(1);
    }

    [Fact]
    public async Task EmptySelectionIsPreservedAndUnsupportedLanguagesAreIgnored()
    {
        _settings.Current = new ClientSettings { TextCorrectionLanguages = [] };
        (await CreateSelection().GetLayoutIdsAsync()).ShouldBeEmpty();
        _settings.Current = new ClientSettings { TextCorrectionLanguages = ["de", "tr", "es"] };
        (await CreateSelection().GetLayoutIdsAsync()).ShouldBe(["es"]);
        await Should.ThrowAsync<ArgumentException>(() => CreateSelection().SetLanguageAsync("tr", true).AsTask());
    }

    [Fact]
    public async Task PausingStopsCorrectionButKeepsTheLanguagesForResuming()
    {
        var selection = CreateSelection();
        var changes = 0;
        selection.Changed += () => changes++;
        await selection.SetLanguageAsync("en", true);
        await selection.SetLanguageAsync("ru", true);

        await selection.SetEnabledAsync(false);
        (await selection.GetLayoutIdsAsync()).ShouldBeEmpty();
        var paused = await selection.GetStateAsync();
        paused.Languages.Select(layout => layout.LanguageId).ShouldBe(["en", "ru"]);
        paused.IsActive.ShouldBeFalse();

        await selection.SetEnabledAsync(true);
        (await selection.GetLayoutIdsAsync()).ShouldBe(["en", "ru"]);
        (await selection.GetStateAsync()).IsActive.ShouldBeTrue();
        changes.ShouldBe(4);
    }

    [Fact]
    public async Task CorrectionIsSwitchedOnUntilPausedAndInactiveWithoutLanguages()
    {
        var state = await CreateSelection().GetStateAsync();
        state.Enabled.ShouldBeTrue();
        state.IsActive.ShouldBeFalse();
    }

    private TextCorrectionLanguages CreateSelection() =>
        new(_settings, _composition.Layouts, _composition.SupportedLayouts);

    private sealed class Settings : IClientSettingsService
    {
        public ClientSettings Current { get; set; } = new();
        public ValueTask<ClientSettings> GetAsync() => ValueTask.FromResult(Current);
        public ValueTask<ClientSettings> UpdateAsync(Func<ClientSettings, ClientSettings> update)
        {
            Current = update(Current);
            return ValueTask.FromResult(Current);
        }
    }
}
