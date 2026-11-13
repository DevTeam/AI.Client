namespace AI.Web.Tests.Components;

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

/// <summary>The local model's connection: its state leads the form and what AI Client manages is not editable.</summary>
public sealed class LocalModelSettingsRenderingTests
{
    [Fact]
    public async Task AReadyModelShowsItsStateAndHidesWhatTheApplicationManages()
    {
        var html = await RenderAsync(new LocalModelStatus("ready", "qwen3.5-2b-local", Name: "Local Qwen3.5 2B", ContextTokens: 8192));

        html.ShouldContain("host-status-dot is-connected");
        html.ShouldContain("Ready");
        html.ShouldContain("runs on this computer");
        html.ShouldContain("This computer, port 52176");
        html.ShouldContain("On this computer");
        html.ShouldNotContain("API key");
        html.ShouldNotContain("Base URL");
        html.ShouldNotContain("Image input");
        html.ShouldNotContain("Prices");
        html.ShouldNotContain("Copy connection as JSON");
    }

    [Fact]
    public async Task AFailedModelExplainsWhyAndOffersARetry()
    {
        const string reason = "No free network port was found on this computer, so the local model cannot start.";
        var html = await RenderAsync(new LocalModelStatus("failed", "qwen3.5-2b-local", reason, "Local Qwen3.5 2B", 8192, CanRetry: true));

        html.ShouldContain("host-status-dot is-failed");
        html.ShouldContain("Not available");
        html.ShouldContain("role=\"alert\"");
        html.ShouldContain(reason);
        html.ShouldContain("Try again");
    }

    [Fact]
    public async Task AComponentLeftOutAtInstallationIsAChoiceNotAnError()
    {
        var html = await RenderAsync(new LocalModelStatus("notInstalled"), enabled: false);

        html.ShouldContain("Not installed");
        html.ShouldContain("keep Local AI selected");
        html.ShouldContain("Remove connection");
        html.ShouldContain("Needs the Local AI component");
        html.ShouldNotContain("role=\"alert\"");
        html.ShouldNotContain("Try again");
    }

    private static async Task<string> RenderAsync(LocalModelStatus status, bool enabled = true)
    {
        await using var services = Services(status, enabled);
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<GlobalSettingsPanel>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(GlobalSettingsPanel.Section)] = "Connections" }))).ToHtmlString());
    }

    private static ServiceProvider Services(LocalModelStatus status, bool enabled)
    {
        var settings = new Mock<IGlobalSettingsApi>();
        settings.Setup(value => value.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new GlobalSettings(
            [new ConnectionSettings(LocalModelStatus.ConnectionId, "Local Qwen3.5 2B", "http://127.0.0.1:52176/v1",
                "qwen3.5-2b-local", enabled, true, true, Capability: 1, ContextWindowTokens: 8192, ReservedOutputTokens: 1024,
                Prices: new AI.Contracts.Usage.TokenPrices(0, 0, 0), ImageInput: ImageInputMode.Disabled)], [], []));
        settings.Setup(value => value.GetLocalModelStatusAsync(It.IsAny<CancellationToken>())).ReturnsAsync(status);
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
            .AddTransient<IToolDefaultDecision, ToolDefaultDecision>()
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
            .AddSingleton(Mock.Of<IPublishedDownloadLinks>())
            .AddSingleton(Mock.Of<IComposerContextPresentation>())
            .BuildServiceProvider();
    }
}
