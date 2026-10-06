namespace AI.Web.Tests.Navigation;

using AI.Contracts.Navigation;
using AI.Web.Navigation;
using AI.Web.Widgets;
using Shouldly;
using Xunit;

public sealed class AppControlHintsTests
{
    [Fact]
    public void EveryGuideTargetShouldHaveHelpAvailableBeforeItsPanelIsRendered()
    {
        var targets = new AppNavigationTargets();
        var hints = new AppControlHints(targets);
        targets.All.Select(target => target.Id).Distinct(StringComparer.Ordinal).Count().ShouldBe(targets.All.Count);
        foreach (var target in targets.All)
        {
            target.Hint.ShouldNotBeNullOrWhiteSpace(target.Id);
            var attributes = hints.Attributes(target.Id);
            attributes["data-app-target"].ShouldBe(target.Id);
            attributes["data-app-hint"].ShouldBe(target.Hint);
            attributes["title"].ShouldBe(target.Hint);
        }
    }

    [Fact]
    public void WidgetDescriptionsAndEveryWidgetsLayoutControlsShouldUseSharedHelp()
    {
        var targets = new AppNavigationTargets();
        var hints = new AppControlHints(targets);
        foreach (var widget in new ChatWidgetCatalog(targets).Widgets)
        {
            widget.Description.ShouldBe(targets.Find("widgets." + widget.Id)!.Hint);
            foreach (var action in new[] { "move", "title", "hide" })
            {
                hints.Attributes($"widgets.{widget.Id}.{action}")["title"].ShouldNotBeNull();
                targets.All.ShouldContain(target => target.Id == $"widgets.{widget.Id}.{action}");
            }
        }
        targets.Find("widgets.unknown.move").ShouldBeNull();
    }

    [Fact]
    public void ChangingSharedHelpShouldUpdateTheTooltipWithoutMaintainingAnotherDescription()
    {
        var target = new AppNavigationTarget("chat.send", "Send", null, ["click"], Hint: "Updated control behavior.");
        var catalog = new Moq.Mock<IAppNavigationTargets>();
        catalog.Setup(value => value.Find(target.Id)).Returns(target);
        var attributes = new AppControlHints(catalog.Object).Attributes(target.Id, "Queue for later");
        attributes["data-app-hint"].ShouldBe(target.Hint);
        attributes["data-app-ui-hint"].ShouldBe("Queue for later");
        attributes["title"].ShouldBe("Queue for later\n\nUpdated control behavior.");
    }

    [Fact]
    public void HelpKeptOnlyForGuidesShouldNotBecomeATooltip()
    {
        var attributes = new AppControlHints(new AppNavigationTargets()).Attributes("chat", nativeTooltip: false);
        attributes["data-app-target"].ShouldBe("chat");
        attributes["data-app-hint"].ShouldBe(new AppNavigationTargets().Find("chat")!.Hint);
        attributes.ContainsKey("title").ShouldBeFalse();
    }

    [Fact]
    public void AnUnregisteredControlShouldNotSilentlyLoseItsHelp() =>
        Should.Throw<ArgumentException>(() => new AppControlHints(new AppNavigationTargets()).Attributes("unknown"));
}
