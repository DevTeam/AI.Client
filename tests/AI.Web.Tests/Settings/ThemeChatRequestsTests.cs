namespace AI.Web.Tests.Settings;

using AI.Contracts.Themes;
using AI.Web.Settings;
using Microsoft.JSInterop;
using Shouldly;
using Xunit;

/// <summary>
/// A chat shapes a theme in small steps through <c>app_theme</c>. What these tests hold down: a preview
/// paints without writing anything, each step continues the last one, saving lands where the model
/// says (in place or as a new theme), and a request the window cannot carry out changes nothing.
/// </summary>
public sealed class ThemeChatRequestsTests
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

        public CustomTheme? LastPainted => Calls.LastOrDefault(call => call.Identifier == "aiClientTheme.applyCustomTheme").Argument as CustomTheme;
    }

    private static (ThemeChatRequests Requests, ThemeService Theme, FakeJSRuntime Js) NewRequests()
    {
        var js = new FakeJSRuntime();
        var theme = new ThemeService(new ClientSettingsService(js), js);
        return (new ThemeChatRequests(theme), theme, js);
    }

    [Fact]
    public async Task ShouldPaintAPreviewWithoutSavingIt()
    {
        var (requests, theme, js) = NewRequests();

        var state = await requests.HandleAsync(new AppThemeRequest(AppThemeRequest.Preview, Name: "Forest", Background: "#142019"));

        state.Error.ShouldBeNull();
        state.Preview.ShouldNotBeNull().Seeds.Background.ShouldBe("#142019");
        state.Preview.Name.ShouldBe("Forest");
        state.Preview.Colors.ShouldContainKey(ThemeToken.Text);
        state.Preview.Findings.ShouldNotBeEmpty();
        js.LastPainted.ShouldNotBeNull().Seeds.Background.ShouldBe("#142019");
        (await theme.GetCustomThemesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task ShouldContinueThePreviewWithOnlyTheSeedsARequestNames()
    {
        var (requests, _, _) = NewRequests();
        await requests.HandleAsync(new AppThemeRequest(AppThemeRequest.Preview, Name: "Forest", Background: "#142019"));

        var state = await requests.HandleAsync(new AppThemeRequest(AppThemeRequest.Preview, Contrast: 80));

        var seeds = state.Preview.ShouldNotBeNull().Seeds;
        seeds.Background.ShouldBe("#142019");
        seeds.Contrast.ShouldBe(80);
        state.Preview.Name.ShouldBe("Forest");
    }

    [Fact]
    public async Task ShouldSaveAnImprovedThemeInPlaceAndACopyAsANewTheme()
    {
        var (requests, theme, _) = NewRequests();
        var original = await theme.SaveCustomThemeAsync(null, "Plum", new CustomThemeSeeds("#231a29"));

        var preview = await requests.HandleAsync(new AppThemeRequest(AppThemeRequest.Preview, ThemeId: original.Id, Saturation: 80));
        preview.Preview.ShouldNotBeNull().BaseThemeId.ShouldBe(original.Id);
        var inPlace = await requests.HandleAsync(new AppThemeRequest(AppThemeRequest.Save, ThemeId: original.Id));

        inPlace.Preview.ShouldBeNull();
        inPlace.Themes.Single().Seeds.Saturation.ShouldBe(80);
        inPlace.ActiveThemeId.ShouldBe(original.Id);

        await requests.HandleAsync(new AppThemeRequest(AppThemeRequest.Preview, Saturation: 20));
        var copy = await requests.HandleAsync(new AppThemeRequest(AppThemeRequest.Save, Name: "Plum mist", Accent: "purple"));

        copy.Themes.Count.ShouldBe(2);
        copy.Themes.Single(saved => saved.Id == original.Id).Seeds.Saturation.ShouldBe(80);
        copy.Themes.Single(saved => saved.Active).Name.ShouldBe("Plum mist");
        copy.Accent.ShouldBe("purple");
    }

    [Fact]
    public async Task ShouldSwitchToAPresetAndDropThePreview()
    {
        var (requests, theme, js) = NewRequests();
        await theme.SaveCustomThemeAsync(null, "Plum", new CustomThemeSeeds("#231a29"));
        await requests.HandleAsync(new AppThemeRequest(AppThemeRequest.Preview, Background: "#f6f4ef"));

        var state = await requests.HandleAsync(new AppThemeRequest(AppThemeRequest.Activate, Preset: "lightgray"));

        state.Error.ShouldBeNull();
        state.Preset.ShouldBe("lightgray");
        state.ActiveThemeId.ShouldBeNull();
        state.Preview.ShouldBeNull();
        js.LastPainted.ShouldBeNull();
    }

    [Fact]
    public async Task ShouldPutTheSavedThemeBackOnDiscard()
    {
        var (requests, theme, js) = NewRequests();
        var saved = await theme.SaveCustomThemeAsync(null, "Plum", new CustomThemeSeeds("#231a29"));
        await requests.HandleAsync(new AppThemeRequest(AppThemeRequest.Preview, Background: "#f6f4ef"));

        var state = await requests.HandleAsync(new AppThemeRequest(AppThemeRequest.Discard));

        state.Preview.ShouldBeNull();
        js.LastPainted.ShouldNotBeNull().Id.ShouldBe(saved.Id);
    }

    [Theory]
    [InlineData("preview", null, null, "not-a-colour", null)]
    [InlineData("preview", null, null, null, "dusk")]
    [InlineData("activate", "missing", null, null, null)]
    [InlineData("activate", null, "violet", null, null)]
    [InlineData("rename", null, null, null, null)]
    public async Task ShouldRefuseWhatItCannotCarryOutAndChangeNothing(string operation, string? themeId, string? preset,
        string? background, string? family)
    {
        var (requests, theme, js) = NewRequests();

        var state = await requests.HandleAsync(new AppThemeRequest(operation, themeId, preset, Background: background, Family: family));

        state.Error.ShouldNotBeNullOrWhiteSpace();
        state.Effect.ShouldBe("Nothing was changed.");
        state.Preview.ShouldBeNull();
        js.Calls.ShouldNotContain(call => call.Identifier.StartsWith("aiClientTheme.", StringComparison.Ordinal));
        (await theme.GetCustomThemesAsync()).ShouldBeEmpty();
    }
}
