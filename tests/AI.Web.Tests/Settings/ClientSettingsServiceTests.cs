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
    }

    [Fact]
    public async Task ShouldReadAnEntryWrittenBeforeAccentExisted()
    {
        var js = new FakeJSRuntime();
        js.Entries[StorageKey] = "{\"theme\":\"light\"}";

        var settings = await new ClientSettingsService(js).GetAsync();

        settings.Theme.ShouldBe(ThemePreference.Light);
        settings.Accent.ShouldBe(AccentColor.Blue);
        settings.CornerRoundnessPercent.ShouldBe(100);
        settings.NotificationSoundEnabled.ShouldBeTrue();
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

        await service.UpdateAsync(settings => settings with { Theme = ThemePreference.Light, Accent = AccentColor.Teal });

        js.Entries[StorageKey].ShouldBe("{\"theme\":\"light\",\"accent\":\"teal\",\"cornerRoundnessPercent\":100,\"notificationSoundEnabled\":true}");
        js.Calls.ShouldContain(call => call.Identifier == "aiClientTheme.saveClientSettings");
        (await new ClientSettingsService(js).GetAsync()).Theme.ShouldBe(ThemePreference.Light);
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
    public async Task ThemeServiceShouldSaveAndApplyThePreference()
    {
        var js = new FakeJSRuntime();
        var theme = new ThemeService(new ClientSettingsService(js), js);

        await theme.SetAsync(ThemePreference.Dark);

        (await theme.GetAsync()).ShouldBe(ThemePreference.Dark);
        js.Calls.ShouldContain(call => call.Identifier == "aiClientTheme.apply" && Equals(call.Args[0], "dark"));
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
