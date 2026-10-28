namespace AI.Web.Tests.Settings;

using System.Text.Json;
using AI.Contracts.Themes;
using AI.Web.Settings;
using Microsoft.JSInterop;
using Shouldly;
using Xunit;

/// <summary>
/// Saving a theme is where the engine, the stored settings and the page meet. What these tests hold
/// down: a saved theme always carries a palette the engine produced, an edit changes the colours the
/// page is painted with, and a removed theme cannot stay on the page.
/// </summary>
public sealed class CustomThemeServiceTests
{
    private const string StorageKey = "ai-client.settings";

    private sealed class FakeJSRuntime : IJSRuntime
    {
        public Dictionary<string, string> Entries { get; } = new();
        public List<(string Identifier, object? Argument)> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add((identifier, args is { Length: > 0 } ? args[0] : null));
            switch (identifier)
            {
                case "localStorage.getItem":
                    return ValueTask.FromResult((TValue)(object?)Entries.GetValueOrDefault((string)args![0]!)!);
                case "aiClientTheme.saveClientSettings":
                    Entries[StorageKey] = (string)args![0]!;
                    break;
            }
            return ValueTask.FromResult(default(TValue)!);
        }
    }

    private static (ThemeService Theme, FakeJSRuntime Js) NewService()
    {
        var js = new FakeJSRuntime();
        return (new ThemeService(new ClientSettingsService(js), js), js);
    }

    [Fact]
    public async Task ShouldSaveThePaletteTheEngineProduces()
    {
        var (theme, js) = NewService();

        var saved = await theme.SaveCustomThemeAsync(null, "Warm dusk", new CustomThemeSeeds("#2b2320"));

        var palette = ThemePalette.Generate(new CustomThemeSeeds("#2b2320"));
        saved.Tokens.ShouldBe(palette.CustomProperties);
        saved.Seeds.ShouldBe(palette.Seeds);
        saved.Family.ShouldBe("dark");
        js.Calls.ShouldContain(call => call.Identifier == "aiClientTheme.applyCustomTheme");
    }

    [Fact]
    public async Task ShouldNameAndListASavedTheme()
    {
        var (theme, _) = NewService();

        var saved = await theme.SaveCustomThemeAsync(null, "  Warm   dusk ", new CustomThemeSeeds("#2b2320"));

        saved.Name.ShouldBe("Warm dusk");
        saved.Id.ShouldNotBeNullOrWhiteSpace();
        (await theme.GetCustomThemesAsync()).ShouldBe([saved]);
        (await theme.GetActiveCustomThemeAsync()).ShouldBe(saved);
    }

    [Fact]
    public async Task ShouldFallBackToTheDefaultNameWhenTheNameIsBlank()
    {
        var (theme, _) = NewService();

        (await theme.SaveCustomThemeAsync(null, "   ", new CustomThemeSeeds("#2b2320"))).Name
            .ShouldBe(CustomTheme.DefaultName);
    }

    [Fact]
    public async Task ShouldReplaceAThemeInPlaceWithoutLosingItsPlaceInTheList()
    {
        var (theme, _) = NewService();
        var first = await theme.SaveCustomThemeAsync(null, "First", new CustomThemeSeeds("#2b2320"));
        var second = await theme.SaveCustomThemeAsync(null, "Second", new CustomThemeSeeds("#101720"));

        var edited = await theme.SaveCustomThemeAsync(first.Id, "First", new CustomThemeSeeds("#2b2320", Contrast: 90));

        edited.Id.ShouldBe(first.Id);
        var themes = await theme.GetCustomThemesAsync();
        themes.Count.ShouldBe(2);
        themes[0].Id.ShouldBe(first.Id);
        themes[1].Id.ShouldBe(second.Id);
        // Two saved themes share the page's data-theme="custom", so the colours are what tells them apart.
        themes[0].Tokens.ShouldNotBe(first.Tokens);
        themes[1].Tokens.ShouldBe(second.Tokens);
    }

    [Fact]
    public async Task ShouldNeverWriteAPaletteFromThePreviewAsASavedTheme()
    {
        var (theme, _) = NewService();
        var stored = await theme.SaveCustomThemeAsync(null, "Stored", new CustomThemeSeeds("#2b2320"));

        // The studio paints what it is choosing under the preview id; saving that must add a theme.
        var added = await theme.SaveCustomThemeAsync(CustomTheme.PreviewId, "Chosen", new CustomThemeSeeds("#101720"));

        added.Id.ShouldNotBe(CustomTheme.PreviewId);
        var themes = await theme.GetCustomThemesAsync();
        themes.Count.ShouldBe(2);
        themes.ShouldContain(candidate => candidate.Id == stored.Id && candidate.Tokens == stored.Tokens);
    }

    [Fact]
    public async Task ShouldSwitchToASavedThemeAndBackToThePreset()
    {
        var (theme, js) = NewService();
        var saved = await theme.SaveCustomThemeAsync(null, "Mine", new CustomThemeSeeds("#2b2320"));

        await theme.SetActiveCustomThemeAsync(null);
        (await theme.GetActiveCustomThemeAsync()).ShouldBeNull();
        js.Calls.ShouldContain(call => call.Identifier == "aiClientTheme.applyCustomTheme" && call.Argument == null);

        await theme.SetActiveCustomThemeAsync(saved.Id);
        (await theme.GetActiveCustomThemeAsync()).ShouldBe(saved);
    }

    [Fact]
    public async Task ChoosingAPresetShouldRemoveTheSavedPaletteFromThePage()
    {
        var (theme, js) = NewService();
        await theme.SaveCustomThemeAsync(null, "Mine", new CustomThemeSeeds("#2b2320"));
        js.Calls.Clear();

        await theme.SetAsync(ThemePreference.LightGray);

        // Without the removal the inline palette would stay on <html> and the preset would be painted
        // underneath colours that are no longer the person's choice.
        js.Calls.ShouldContain(call => call.Identifier == "aiClientTheme.applyCustomTheme" && call.Argument == null);
        js.Calls.ShouldContain(call => call.Identifier == "aiClientTheme.apply" && Equals(call.Argument, "lightgray"));
        (await theme.GetActiveCustomThemeAsync()).ShouldBeNull();
        (await theme.GetAsync()).ShouldBe(ThemePreference.LightGray);
    }

    [Fact]
    public async Task ShouldDeleteAThemeAndReturnToThePresetWhenItWasInUse()
    {
        var (theme, js) = NewService();
        var saved = await theme.SaveCustomThemeAsync(null, "Mine", new CustomThemeSeeds("#2b2320"));
        await theme.SetAsync(ThemePreference.Dark);
        await theme.SetActiveCustomThemeAsync(saved.Id);
        js.Calls.Clear();

        await theme.DeleteCustomThemeAsync(saved.Id);

        (await theme.GetCustomThemesAsync()).ShouldBeEmpty();
        (await theme.GetActiveCustomThemeAsync()).ShouldBeNull();
        js.Calls.ShouldContain(call => call.Identifier == "aiClientTheme.applyCustomTheme" && call.Argument == null);
    }

    [Fact]
    public async Task ShouldKeepTheActiveThemeWhenAnotherOneIsDeleted()
    {
        var (theme, _) = NewService();
        var inUse = await theme.SaveCustomThemeAsync(null, "In use", new CustomThemeSeeds("#2b2320"));
        var other = await theme.SaveCustomThemeAsync(null, "Other", new CustomThemeSeeds("#101720"));
        await theme.SetActiveCustomThemeAsync(inUse.Id);

        await theme.DeleteCustomThemeAsync(other.Id);

        (await theme.GetActiveCustomThemeAsync()).ShouldBe(inUse);
    }

    [Fact]
    public async Task ShouldPaintAPreviewWithoutStoringIt()
    {
        var (theme, js) = NewService();
        var stored = await theme.SaveCustomThemeAsync(null, "Stored", new CustomThemeSeeds("#2b2320"));

        var preview = CustomTheme.Create(CustomTheme.PreviewId, "Choosing", new CustomThemeSeeds("#101720"));
        await theme.PreviewCustomThemeAsync(preview);

        js.Calls.ShouldContain(call => call.Identifier == "aiClientTheme.applyCustomTheme" && ReferenceEquals(call.Argument, preview));
        (await theme.GetCustomThemesAsync()).ShouldBe([stored]);
        (await theme.GetActiveCustomThemeAsync()).ShouldBe(stored);
    }

    [Fact]
    public async Task ShouldStoreEveryThemableRoleAndNoReservedOne()
    {
        var (theme, _) = NewService();

        var saved = await theme.SaveCustomThemeAsync(null, "Mine", new CustomThemeSeeds("#2b2320"));

        saved.Tokens.Count.ShouldBe(ThemeToken.All.Count);
        foreach (var role in ThemeToken.All) saved.Tokens.ShouldContainKey(CustomTheme.CssPrefix + role);
        foreach (var reserved in ThemeToken.Reserved.Keys)
            saved.Tokens.ShouldNotContainKey(CustomTheme.CssPrefix + reserved);
        foreach (var layer in ThemeToken.ReservedContextLayers)
            saved.Tokens.ShouldNotContainKey(CustomTheme.CssPrefix + layer);
    }

    [Fact]
    public async Task ShouldWriteTheStoredThemeInTheShapeThemeScriptReads()
    {
        var (theme, js) = NewService();

        await theme.SaveCustomThemeAsync(null, "Warm dusk", new CustomThemeSeeds("#2b2320", Contrast: 70));

        using var entry = JsonDocument.Parse(js.Entries[StorageKey]);
        var saved = entry.RootElement.GetProperty("customThemes")[0];
        saved.GetProperty("name").GetString().ShouldBe("Warm dusk");
        // js/theme.js matches this attribute value verbatim; it must not depend on how .NET spells an enum.
        saved.GetProperty("family").GetString().ShouldBe("dark");
        saved.GetProperty("seeds").GetProperty("contrast").GetInt32().ShouldBe(70);
        saved.GetProperty("tokens").TryGetProperty("--color-bg", out var background).ShouldBeTrue();
        background.GetString().ShouldBe("#2b2320");
        entry.RootElement.GetProperty("activeCustomThemeId").GetString().ShouldBe(saved.GetProperty("id").GetString());
    }

    [Fact]
    public async Task ShouldSpellALightFamilyTheWayThePageReadsIt()
    {
        var (theme, _) = NewService();

        var saved = await theme.SaveCustomThemeAsync(null, "Paper", new CustomThemeSeeds("#f7f2e8"));

        saved.Family.ShouldBe("light");
        saved.Seeds.Family.ShouldBe(ThemeFamily.Light);
    }

    [Fact]
    public async Task ShouldResolveAnOpenFamilyFromTheBackground()
    {
        var (theme, _) = NewService();

        var saved = await theme.SaveCustomThemeAsync(null, "Mid", new CustomThemeSeeds("#8a8a8a", Family: null));

        // Whatever a mid grey resolves to, the studio shows back the choice it was built from.
        saved.Seeds.Family.ShouldNotBeNull();
        saved.Family.ShouldBe(saved.Seeds.Family == ThemeFamily.Light ? "light" : "dark");
    }
}
