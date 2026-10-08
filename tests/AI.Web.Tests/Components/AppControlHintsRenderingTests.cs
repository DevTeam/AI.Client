namespace AI.Web.Tests.Components;

using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using AI.Contracts.Navigation;
using AI.Contracts.Settings;
using AI.TextCorrection;
using AI.Web.Components;
using AI.Web.Composer;
using AI.Web.Navigation;
using AI.Web.Notifications;
using AI.Web.Settings;
using AI.Web.Updates;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using Shouldly;
using Xunit;

public sealed class AppControlHintsRenderingTests
{
    [Theory]
    [InlineData("Settings")]
    [InlineData("Connections")]
    public async Task SettingsControlsShouldRenderTheSameHelpTheGuideReceives(string section)
    {
        await using var services = Services();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<GlobalSettingsPanel>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(GlobalSettingsPanel.Section)] = section }));
            var html = component.ToHtmlString();
            var targets = new AppNavigationTargets();
            var rendered = Regex.Matches(html, "<[^>]+data-app-target=\"([^\"]+)\"[^>]*>");
            rendered.Count.ShouldBeGreaterThan(10);
            foreach (System.Text.RegularExpressions.Match match in rendered)
            {
                var target = targets.Find(match.Groups[1].Value).ShouldNotBeNull();
                match.Value.ShouldContain($"data-app-hint=\"{HtmlEncoder.Default.Encode(target.Hint!)}\"");
                // The drawer is a region, not a control: hovering anywhere in it must not raise its help.
                if (match.Value.StartsWith("<aside", StringComparison.Ordinal)) match.Value.ShouldNotContain("title=\"");
                else match.Value.ShouldContain("title=\"");
            }
            foreach (var target in targets.All.Where(target => target.Section == section))
                html.ShouldContain($"data-app-target=\"{target.Id}\"");
        });
    }

    [Theory]
    [InlineData(ComposerSendButton.SendMode.Send, "Send message")]
    [InlineData(ComposerSendButton.SendMode.Queue, "Queue for later")]
    [InlineData(ComposerSendButton.SendMode.Fork, "Send in a forked session")]
    [InlineData(ComposerSendButton.SendMode.EditFork, "Create edited branch")]
    [InlineData(ComposerSendButton.SendMode.Replace, "Replace branch now")]
    [InlineData(ComposerSendButton.SendMode.Schedule, "Schedule instead of sending")]
    public async Task SendButtonShouldExposeItsCurrentActionWithoutPuttingSharedHelpInTheTooltip(
        ComposerSendButton.SendMode mode, string label)
    {
        await using var services = Services();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<ComposerSendButton>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(ComposerSendButton.ActiveMode)] = mode }));
            var html = component.ToHtmlString();
            html.ShouldContain($"aria-label=\"{label}\"");
            html.ShouldContain($"data-app-ui-hint=\"{label}\"");
            html.ShouldContain("aria-describedby=\"composer-send-tooltip\"");
            html.ShouldContain($"data-app-hint=\"{HtmlEncoder.Default.Encode(new AppNavigationTargets().Find("chat.send")!.Hint!)}\"");
            html.ShouldNotContain("composer-send-tooltip-help");
            html.ShouldNotContain("title=\"");
        });
    }

    [Fact]
    public async Task SendButtonShouldAddToTheTurnInFlightAndListEscapeAsStop()
    {
        await using var services = Services();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var idle = (await renderer.RenderComponentAsync<ComposerSendButton>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(ComposerSendButton.ActiveMode)] = ComposerSendButton.SendMode.Send }))).ToHtmlString();
            idle.ShouldContain("aria-label=\"Send message\"");
            idle.ShouldNotContain("<kbd>Esc</kbd>");
            idle.ShouldNotContain("aside");

            var running = (await renderer.RenderComponentAsync<ComposerSendButton>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(ComposerSendButton.ActiveMode)] = ComposerSendButton.SendMode.Send,
                    [nameof(ComposerSendButton.TurnInFlight)] = true
                }))).ToHtmlString();
            running.ShouldContain("aria-label=\"Add to the current turn\"");
            running.ShouldContain("Stop the turn");
            running.ShouldContain("<kbd>Esc</kbd>");
        });
    }

    private static ServiceProvider Services()
    {
        var settings = new Mock<IGlobalSettingsApi>();
        settings.Setup(value => value.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new GlobalSettings(
            [new ConnectionSettings(Guid.NewGuid(), "Example", string.Empty, "example", true, true, false)], [], []));
        var preferences = new Mock<IClientSettingsService>();
        preferences.Setup(value => value.GetAsync()).Returns(new ValueTask<ClientSettings>(new ClientSettings()));
        var languages = new Mock<ITextCorrectionLanguages>();
        languages.Setup(value => value.Available).Returns(new KeyboardLayouts().All);
        languages.Setup(value => value.GetStateAsync()).Returns(new ValueTask<TextCorrectionState>(new TextCorrectionState([], true)));
        return new ServiceCollection()
            .AddTransient<IAppNavigationTargets, AppNavigationTargets>()
            .AddTransient<IAppControlHints, AppControlHints>()
            .AddSingleton(settings.Object)
            .AddSingleton(preferences.Object)
            .AddSingleton(languages.Object)
            .AddSingleton<IConnectionContextLimitsResolver, ConnectionContextLimitsResolver>()
            .AddSingleton(Mock.Of<ITextCorrectionPreparation>())
            .AddSingleton(Mock.Of<INotificationService>())
            .AddSingleton(Mock.Of<IThemeService>())
            .AddSingleton(Mock.Of<AI.Web.IClientMode>())
            .AddSingleton(Mock.Of<AI.Web.IHostConnection>())
            .AddSingleton(Mock.Of<ISettingsTransferCodec>())
            .AddSingleton(Mock.Of<ISettingsImportPlanner>())
            .AddSingleton<ISettingsListFilter, SettingsListFilter>()
            .AddSingleton(Mock.Of<IJSRuntime>())
            .AddSingleton(Mock.Of<IUpdateClient>())
            .AddSingleton(Mock.Of<IComposerContextPresentation>())
            .BuildServiceProvider();
    }
}
