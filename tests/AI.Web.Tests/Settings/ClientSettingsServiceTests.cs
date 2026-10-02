namespace AI.Web.Tests.Settings;

using AI.Web.Settings;
using Microsoft.JSInterop;
using Shouldly;
using Xunit;

public class ClientSettingsServiceTests
{
    // js/theme.js reads this entry on page load; the key and shape are a contract with it.
    private const string StorageKey = "ai-client.settings";

    private sealed class FakeJSRuntime : IJSRuntime
    {
        public Dictionary<string, string> Entries { get; } = new();
        public List<(string Identifier, IReadOnlyList<object?> Args)> Calls { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add((identifier, args?.ToArray() ?? []));
            switch (identifier)
            {
                case "localStorage.getItem":
                    return ValueTask.FromResult((TValue)(object?)Entries.GetValueOrDefault((string)args![0]!)!);
                case "localStorage.setItem":
                    Entries[(string)args![0]!] = (string)args[1]!;
                    break;
                case "aiClientTheme.saveClientSettings":
                    Entries[StorageKey] = (string)args![0]!;
                    break;
            }
            return ValueTask.FromResult(default(TValue)!);
        }
    }

    [Fact]
    public async Task ShouldReturnDefaultsWhenNothingIsSaved()
    {
        var service = new ClientSettingsService(new FakeJSRuntime());

        (await service.GetAsync()).Theme.ShouldBe(ThemePreference.System);
        (await service.GetAsync()).Accent.ShouldBe(AccentColor.Blue);
        (await service.GetAsync()).CornerRoundnessPercent.ShouldBe(100);
        (await service.GetAsync()).NotificationSoundEnabled.ShouldBeTrue();
        (await service.GetAsync()).OtherSoundsEnabled.ShouldBeTrue();
        (await service.GetAsync()).ShowContextWindowUsage.ShouldBeTrue();
    }

    [Theory]
    [InlineData("{\"theme\":\"light\"}")]
    [InlineData("{\"theme\":\"darkBlue\"}")]
    public async Task ShouldReadAnEntryWrittenBeforeAccentExisted(string saved)
    {
        var js = new FakeJSRuntime();
        js.Entries[StorageKey] = saved;

        var settings = await new ClientSettingsService(js).GetAsync();

        settings.Theme.ShouldBe(saved.Contains("darkBlue") ? ThemePreference.DarkBlue : ThemePreference.Light);
        settings.Accent.ShouldBe(AccentColor.Blue);
        settings.CornerRoundnessPercent.ShouldBe(100);
        settings.NotificationSoundEnabled.ShouldBeTrue();
        settings.OtherSoundsEnabled.ShouldBeTrue();
        settings.ShowContextWindowUsage.ShouldBeTrue();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"theme\":\"sepia\"}")]
    public async Task ShouldFallBackToDefaultsWhenTheEntryCannotBeRead(string json)
    {
        var js = new FakeJSRuntime();
        js.Entries[StorageKey] = json;

        (await new ClientSettingsService(js).GetAsync()).Theme.ShouldBe(ThemePreference.System);
    }

    [Fact]
    public async Task ShouldSaveInTheShapeThemeScriptReads()
    {
        var js = new FakeJSRuntime();
        var service = new ClientSettingsService(js);

        await service.UpdateAsync(settings => settings with
        {
            Theme = ThemePreference.Light,
            Accent = AccentColor.Teal,
            OtherSoundsEnabled = false,
            ShowContextWindowUsage = false
        });

        using var saved = System.Text.Json.JsonDocument.Parse(js.Entries[StorageKey]);
        saved.RootElement.GetProperty("theme").GetString().ShouldBe("light");
        saved.RootElement.GetProperty("accent").GetString().ShouldBe("teal");
        saved.RootElement.GetProperty("otherSoundsEnabled").GetBoolean().ShouldBeFalse();
        saved.RootElement.GetProperty("showContextWindowUsage").GetBoolean().ShouldBeFalse();
        js.Calls.ShouldContain(call => call.Identifier == "aiClientTheme.saveClientSettings");
        (await new ClientSettingsService(js).GetAsync()).Theme.ShouldBe(ThemePreference.Light);
        (await new ClientSettingsService(js).GetAsync()).ShowContextWindowUsage.ShouldBeFalse();
        (await new ClientSettingsService(js).GetAsync()).OtherSoundsEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task ShouldKeepTheWidgetColumnAcrossReads()
    {
        var js = new FakeJSRuntime();

        await new ClientSettingsService(js).UpdateAsync(settings => settings with
        {
            ChatWidgetsOpen = true,
            ChatWidgets = [new AI.Web.Widgets.ChatWidgetPreference("chat-usage", Collapsed: true)]
        });

        var read = await new ClientSettingsService(js).GetAsync();
        read.ChatWidgetsOpen.ShouldBeTrue();
        read.ChatWidgets.ShouldBe([new AI.Web.Widgets.ChatWidgetPreference("chat-usage", Collapsed: true)]);
    }

    [Fact]
    public async Task ShouldReadStorageOnlyOnce()
    {
        var js = new FakeJSRuntime();
        var service = new ClientSettingsService(js);

        await service.GetAsync();
        await service.UpdateAsync(settings => settings with { Theme = ThemePreference.Dark });
        (await service.GetAsync()).Theme.ShouldBe(ThemePreference.Dark);

        js.Calls.Count(call => call.Identifier == "localStorage.getItem").ShouldBe(1);
    }

    [Fact]
    public async Task ShouldSpellDarkBlueTheWayThePageReadsIt()
    {
        var js = new FakeJSRuntime();
        var service = new ClientSettingsService(js);

        await service.UpdateAsync(settings => settings with { Theme = ThemePreference.DarkBlue });

        // js/theme.js lowercases the value before matching it, and the enum is camelCased here.
        js.Entries[StorageKey].ShouldContain("\"theme\":\"darkBlue\"");
        (await service.GetAsync()).Theme.ShouldBe(ThemePreference.DarkBlue);
    }

    [Fact]
    public async Task ThemeServiceShouldSaveAndApplyThePreference()
    {
        var js = new FakeJSRuntime();
        var theme = new ThemeService(new ClientSettingsService(js), js);

        await theme.SetAsync(ThemePreference.Dark);

        (await theme.GetAsync()).ShouldBe(ThemePreference.Dark);
        js.Calls.ShouldContain(call => call.Identifier == "aiClientTheme.apply" && Equals(call.Args[0], "dark"));
    }

    [Fact]
    public async Task ThemeServiceShouldApplyDarkBlueAsThePageNamesIt()
    {
        var js = new FakeJSRuntime();
        var theme = new ThemeService(new ClientSettingsService(js), js);

        await theme.SetAsync(ThemePreference.DarkBlue);

        (await theme.GetAsync()).ShouldBe(ThemePreference.DarkBlue);
        // Must be the data-theme attribute value app.css and Desktop's variant are keyed on.
        js.Calls.ShouldContain(call => call.Identifier == "aiClientTheme.apply" && Equals(call.Args[0], "darkblue"));
    }

    [Fact]
    public async Task ThemeServiceShouldApplyGrayAsThePageNamesIt()
    {
        var js = new FakeJSRuntime();
        var theme = new ThemeService(new ClientSettingsService(js), js);

        await theme.SetAsync(ThemePreference.Gray);

        (await theme.GetAsync()).ShouldBe(ThemePreference.Gray);
        // Must be the data-theme attribute value app.css and Desktop's variant are keyed on.
        js.Calls.ShouldContain(call => call.Identifier == "aiClientTheme.apply" && Equals(call.Args[0], "gray"));
    }

    [Fact]
    public async Task ThemeServiceShouldApplyLightGrayAsThePageNamesIt()
    {
        var js = new FakeJSRuntime();
        var theme = new ThemeService(new ClientSettingsService(js), js);

        await theme.SetAsync(ThemePreference.LightGray);

        (await theme.GetAsync()).ShouldBe(ThemePreference.LightGray);
        // Must be the data-theme attribute value app.css and Desktop's variant are keyed on.
        js.Calls.ShouldContain(call => call.Identifier == "aiClientTheme.apply" && Equals(call.Args[0], "lightgray"));
    }

    [Fact]
    public async Task ThemeServiceShouldSaveAndApplyTheAccent()
    {
        var js = new FakeJSRuntime();
        var theme = new ThemeService(new ClientSettingsService(js), js);

        await theme.SetAccentAsync(AccentColor.Purple);

        (await theme.GetAccentAsync()).ShouldBe(AccentColor.Purple);
        js.Calls.ShouldContain(call => call.Identifier == "aiClientTheme.applyAccent" && Equals(call.Args[0], "purple"));
    }

    [Fact]
    public async Task ThemeServiceShouldSaveAndApplyCornerRoundness()
    {
        var js = new FakeJSRuntime();
        var theme = new ThemeService(new ClientSettingsService(js), js);

        await theme.SetCornerRoundnessAsync(175);

        (await theme.GetCornerRoundnessAsync()).ShouldBe(175);
        js.Calls.ShouldContain(call => call.Identifier == "aiClientTheme.applyCornerRoundness" && Equals(call.Args[0], 175));
        (await new ClientSettingsService(js).GetAsync()).CornerRoundnessPercent.ShouldBe(175);
    }
}
