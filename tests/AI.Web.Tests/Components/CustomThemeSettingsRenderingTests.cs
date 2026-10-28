namespace AI.Web.Tests.Components;

using AI.Contracts.Navigation;
using AI.Contracts.Themes;
using AI.Web.Components;
using AI.Web.Navigation;
using AI.Web.Notifications;
using AI.Web.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

/// <summary>
/// The studio is the only place a person builds a theme, and it has to be usable before anything is
/// saved: its controls carry help the guide can point at, it opens on the theme in use, and it says out
/// loud when a background cannot carry readable text instead of quietly painting one.
/// </summary>
public sealed class CustomThemeSettingsRenderingTests
{
    private static CustomTheme Theme(string name = "Warm dusk") =>
        CustomTheme.Create("theme-1", name, new CustomThemeSeeds("#2b2320"));

    private static ServiceProvider Services(CustomTheme? active)
    {
        var theme = new Mock<IThemeService>();
        theme.Setup(value => value.GetCustomThemesAsync())
            .Returns(new ValueTask<IReadOnlyList<CustomTheme>>(active is null ? [] : [active]));
        theme.Setup(value => value.GetActiveCustomThemeAsync())
            .Returns(new ValueTask<CustomTheme?>(active));
        theme.Setup(value => value.PreviewCustomThemeAsync(It.IsAny<CustomTheme>())).Returns(ValueTask.CompletedTask);
        theme.Setup(value => value.SetActiveCustomThemeAsync(It.IsAny<string?>())).Returns(ValueTask.CompletedTask);
        return new ServiceCollection()
            .AddTransient<IAppNavigationTargets, AppNavigationTargets>()
            .AddTransient<IAppControlHints, AppControlHints>()
            .AddSingleton(theme.Object)
            .AddSingleton(Mock.Of<INotificationService>())
            .AddSingleton(Mock.Of<ISettingsListFilter>())
            .BuildServiceProvider();
    }

    [Fact]
    public async Task ShouldRenderEveryGuideTargetWithItsOwnHelp()
    {
        await using var services = Services(Theme());
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var html = (await renderer.RenderComponentAsync<CustomThemeSettings>(ParameterView.Empty)).ToHtmlString();
            var targets = new AppNavigationTargets().All
                .Where(target => target.Id.StartsWith("settings.appearance.custom_theme", StringComparison.Ordinal))
                .ToArray();
            targets.Length.ShouldBeGreaterThan(5);
            foreach (var target in targets)
            {
                html.ShouldContain($"data-app-target=\"{target.Id}\"");
                html.ShouldContain($"data-app-hint=\"{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(target.Hint!)}\"");
                target.Section.ShouldBe("Settings");
            }
        });
    }

    [Fact]
    public async Task ShouldOpenOnTheThemeInUseAndShowItsSeeds()
    {
        var active = Theme();
        await using var services = Services(active);
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var html = (await renderer.RenderComponentAsync<CustomThemeSettings>(ParameterView.Empty)).ToHtmlString();

            html.ShouldContain("Warm dusk");
            html.ShouldContain("value=\"#2b2320\"");
            html.ShouldContain("checked");
        });
    }

    [Fact]
    public async Task ShouldOfferTheDefaultsWhenNoThemeIsSavedYet()
    {
        await using var services = Services(null);
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var html = (await renderer.RenderComponentAsync<CustomThemeSettings>(ParameterView.Empty)).ToHtmlString();

            // Nothing saved yet: the form offers a starting point instead of an empty list of nothing.
            html.ShouldContain($"value=\"{CustomTheme.DefaultBackground}\"");
            html.ShouldContain("Add theme");
            html.ShouldNotContain("checked");
        });
    }

    [Fact]
    public async Task ShouldNotPreviewAThemeBeforeThePersonChangesAnything()
    {
        var theme = new Mock<IThemeService>(MockBehavior.Strict);
        theme.Setup(value => value.GetCustomThemesAsync()).Returns(new ValueTask<IReadOnlyList<CustomTheme>>([]));
        theme.Setup(value => value.GetActiveCustomThemeAsync()).Returns(new ValueTask<CustomTheme?>((CustomTheme?)null));
        theme.Setup(value => value.PreviewCustomThemeAsync(It.IsAny<CustomTheme?>())).Returns(ValueTask.CompletedTask);
        await using var services = new ServiceCollection()
            .AddTransient<IAppNavigationTargets, AppNavigationTargets>()
            .AddTransient<IAppControlHints, AppControlHints>()
            .AddSingleton(theme.Object)
            .AddSingleton(Mock.Of<INotificationService>())
            .AddSingleton(Mock.Of<ISettingsListFilter>())
            .BuildServiceProvider();
        var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<CustomThemeSettings>(ParameterView.Empty);
        });

        // Opening the studio must not repaint the page: the person has not asked for a new theme yet.
        theme.Verify(value => value.PreviewCustomThemeAsync(It.IsAny<CustomTheme?>()), Times.Never);
        theme.Verify(value => value.SetActiveCustomThemeAsync(It.IsAny<string?>()), Times.Never);

        // Closing it puts the saved palette back, which drops what was only being tried out.
        await renderer.DisposeAsync();
        theme.Verify(value => value.PreviewCustomThemeAsync(null), Times.Once);
        theme.Verify(value => value.SetActiveCustomThemeAsync(It.IsAny<string?>()), Times.Never);
    }
}
