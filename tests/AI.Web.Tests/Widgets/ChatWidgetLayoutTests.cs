namespace AI.Web.Tests.Widgets;

using AI.Web.Widgets;
using Moq;
using Shouldly;
using Xunit;

public class ChatWidgetLayoutTests
{
    private readonly ChatWidgetLayout _layout;

    public ChatWidgetLayoutTests()
    {
        ChatWidgetDefinition[] widgets = [Widget("a"), Widget("b"), Widget("c"), Widget("d")];
        var catalog = new Mock<IChatWidgetCatalog>();
        catalog.SetupGet(item => item.Widgets).Returns(widgets);
        catalog.Setup(item => item.Find(It.IsAny<string>())).Returns((string id) => widgets.FirstOrDefault(widget => widget.Id == id));
        _layout = new ChatWidgetLayout(catalog.Object);
    }

    [Fact]
    public void ShouldKeepTheSavedOrderAddNewWidgetsLastAndDropUnknownOnes()
    {
        var arranged = _layout.Arrange([new("c", Collapsed: true), new("gone"), new("a", Hidden: true), new("c")]);

        arranged.Select(item => item.Id).ShouldBe(["c", "a", "b", "d"]);
        arranged[0].Collapsed.ShouldBeTrue();
        arranged[1].Hidden.ShouldBeTrue();
    }

    [Fact]
    public void ShouldShowEveryWidgetInCatalogOrderWhenNothingWasSaved() =>
        _layout.Arrange(null).ShouldBe([new("a"), new("b"), new("c"), new("d")]);

    [Theory]
    [InlineData("a", "c", "b,a,c,d")]
    [InlineData("d", "a", "d,a,b,c")]
    [InlineData("a", null, "b,c,d,a")]
    [InlineData("b", "b", "a,b,c,d")]
    public void ShouldMoveAWidgetBeforeAnother(string id, string? beforeId, string expected) =>
        string.Join(',', _layout.Move(_layout.Arrange(null), id, beforeId).Select(item => item.Id)).ShouldBe(expected);

    [Fact]
    public void ShouldStepOverHiddenWidgetsWhenMovingByKeyboard()
    {
        IReadOnlyList<ChatWidgetPreference> widgets = [new("a"), new("b", Hidden: true), new("c"), new("d")];

        var down = _layout.MoveBy(widgets, "a", 1);
        var up = _layout.MoveBy(widgets, "d", -1);
        var edge = _layout.MoveBy(widgets, "a", -1);

        down.Select(item => item.Id).ShouldBe(["b", "c", "a", "d"]);
        up.Select(item => item.Id).ShouldBe(["a", "b", "d", "c"]);
        edge.ShouldBeSameAs(widgets);
    }

    [Fact]
    public void ShouldChangeOnlyTheNamedWidget()
    {
        var updated = _layout.Update(_layout.Arrange(null), "b", item => item with { Id = "x", Hidden = true });

        updated.ShouldBe([new("a"), new("b", Hidden: true), new("c"), new("d")]);
    }

    [Fact]
    public void ShouldCollapseEveryVisibleWidgetWhenAsked()
    {
        IReadOnlyList<ChatWidgetPreference> widgets =
        [
            new("a"), new("b", Collapsed: true), new("c", Hidden: true), new("d", Collapsed: false)
        ];

        var collapsed = _layout.SetCollapsed(widgets, true);

        collapsed.ShouldBe([new("a", Collapsed: true), new("b", Collapsed: true), new("c", Hidden: true), new("d", Collapsed: true)]);
    }

    [Fact]
    public void ShouldExpandEveryVisibleWidgetWhenAsked()
    {
        IReadOnlyList<ChatWidgetPreference> widgets =
        [
            new("a", Collapsed: true), new("b"), new("c", Hidden: true, Collapsed: true), new("d", Collapsed: true)
        ];

        var expanded = _layout.SetCollapsed(widgets, false);

        expanded.ShouldBe([new("a"), new("b"), new("c", Hidden: true, Collapsed: true), new("d")]);
    }

    [Fact]
    public void ShouldLeaveHiddenWidgetsUntouchedWhenSettingCollapsed()
    {
        IReadOnlyList<ChatWidgetPreference> widgets =
        [
            new("a"), new("hidden-one", Hidden: true, Collapsed: true), new("b", Hidden: true)
        ];

        var updated = _layout.SetCollapsed(widgets, true);

        updated[1].ShouldBe(new("hidden-one", Hidden: true, Collapsed: true));
        updated[2].ShouldBe(new("b", Hidden: true));
    }

    private static ChatWidgetDefinition Widget(string id) => new(id, id, "gauge", id);
}
