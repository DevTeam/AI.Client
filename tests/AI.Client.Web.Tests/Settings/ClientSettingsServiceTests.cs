namespace AI.Client.Web.Tests.Settings;

using AI.Client.Web.Settings;
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
            }
            return ValueTask.FromResult(default(TValue)!);
        }
    }

    [Fact]
    public async Task ShouldReturnDefaultsWhenNothingIsSaved()
    {
        var service = new ClientSettingsService(new FakeJSRuntime());

        (await service.GetAsync()).Theme.ShouldBe(ThemePreference.System);
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

        await service.UpdateAsync(settings => settings with { Theme = ThemePreference.Light });

        js.Entries[StorageKey].ShouldBe("{\"theme\":\"light\"}");
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
}
