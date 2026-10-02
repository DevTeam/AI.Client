namespace AI.Web.Tests.Components;

using AI.Contracts.Navigation;
using AI.TextCorrection;
using AI.Web.Components;
using AI.Web.Notifications;
using AI.Web.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

public sealed class CorrectionLanguagesSettingsRenderingTests
{
    [Fact]
    public async Task GuideTargetsResolveToRenderedControlsWithoutChangingLanguageSelection()
    {
        var languages = new Mock<ITextCorrectionLanguages>();
        languages.Setup(value => value.Available).Returns(new KeyboardLayouts().All);
        languages.Setup(value => value.GetStateAsync()).Returns(new ValueTask<TextCorrectionState>(new TextCorrectionState([], true)));
        var preparation = new Mock<ITextCorrectionPreparation>(MockBehavior.Strict);
        var registrations = new ServiceCollection();
        registrations.AddSingleton(languages.Object);
        registrations.AddSingleton(preparation.Object);
        registrations.AddSingleton(Mock.Of<INotificationService>());
        await using var services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<CorrectionLanguagesSettings>(ParameterView.Empty);
            var html = component.ToHtmlString();
            var targets = new AppNavigationTargets().All.Where(target => target.Id.StartsWith("settings.chat.text_correction", StringComparison.Ordinal));
            foreach (var target in targets)
            {
                html.ShouldContain($"data-app-target=\"{target.Id}\"");
                target.Section.ShouldBe("Settings");
                target.Actions.ShouldBe(["show", "hover"]);
            }
        });
        languages.Verify(value => value.SetLanguageAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        languages.Verify(value => value.SetEnabledAsync(It.IsAny<bool>()), Times.Never);
        preparation.VerifyNoOtherCalls();
    }
}
