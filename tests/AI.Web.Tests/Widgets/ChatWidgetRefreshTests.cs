namespace AI.Web.Tests.Widgets;

using System.Linq.Expressions;
using System.Reflection;
using AI.Contracts.Chats;
using AI.Contracts.Runs;
using AI.Contracts.Usage;
using AI.Contracts.Workspace;
using AI.Web.Components;
using AI.Web.Widgets;
using Microsoft.AspNetCore.Components;
using Moq;
using Shouldly;
using Xunit;

public sealed class ChatWidgetRefreshTests
{
    [Theory]
    [InlineData("Tools")]
    [InlineData("Knowledge")]
    [InlineData("Files")]
    [InlineData("Performance")]
    [InlineData("Subtasks")]
    [InlineData("Timeline")]
    [InlineData("Branches")]
    public void ShouldReuseStatisticsUntilMessagesChange(string widget)
    {
        var (component, calculator) = Create(widget);
        ApplyParameters(component);
        ApplyParameters(component);
        ApplyParameters(component);
        calculator.Invocations.Count.ShouldBe(1);

        component.GetType().GetProperty("Messages")!.SetValue(component,
            new[] { new ChatMessageView(Guid.NewGuid(), null, "User", "Next turn", DateTimeOffset.UtcNow) });
        ApplyParameters(component);
        calculator.Invocations.Count.ShouldBe(2);
        ApplyParameters(component);
        calculator.Invocations.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("Performance")]
    [InlineData("Subtasks")]
    [InlineData("Timeline")]
    public void ShouldRefreshWhenGenerationStops(string widget)
    {
        var (component, calculator) = Create(widget);
        ApplyParameters(component);
        component.GetType().GetProperty("IsGenerating")!.SetValue(component, true);
        ApplyParameters(component);
        calculator.Invocations.Count.ShouldBe(2);
    }

    // Exercise the lifecycle directly without a DOM renderer: the regression is repeated
    // calculator work when a parent reassigns the same component parameters.
    private static void ApplyParameters(ComponentBase component) =>
        component.GetType().GetMethod("OnParametersSet", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);

    private static (ComponentBase Component, Mock Calculator) Create(string widget) => widget switch
    {
        "Tools" => Create<ChatToolsWidget, IChatToolStatisticsCalculator, ChatToolStatistics>(
            c => c.Calculate(It.IsAny<IReadOnlyList<ChatMessageView>>(), It.IsAny<ChatRunSnapshot?>(), It.IsAny<ChatWidgetScope>()),
            new ChatToolStatistics([], new Dictionary<ChatToolOutcome, int>(), 0, 0, 0, false)),
        "Knowledge" => Create<ChatKnowledgeWidget, IChatKnowledgeStatisticsCalculator, ChatKnowledgeStatistics>(
            c => c.Calculate(It.IsAny<IReadOnlyList<ChatMessageView>>(), It.IsAny<ChatRunSnapshot?>(), It.IsAny<ChatWidgetScope>()),
            ChatKnowledgeStatistics.Empty),
        "Files" => Create<ChatFilesWidget, IChatFileStatisticsCalculator, ChatFileStatistics>(
            c => c.Calculate(It.IsAny<IReadOnlyList<ChatMessageView>>(), It.IsAny<WorkspaceChangeSet?>(), It.IsAny<ChatWidgetScope>()),
            ChatFileStatistics.Empty),
        "Performance" => Create<ChatPerformanceWidget, IChatPerformanceCalculator, PerformanceStatistics>(
            c => c.Calculate(It.IsAny<ChatTokenUsage?>(), It.IsAny<TurnTokenUsage?>(), It.IsAny<IReadOnlyList<ChatMessageView>>(), It.IsAny<bool>(), It.IsAny<ChatWidgetScope>()),
            PerformanceStatistics.Empty),
        "Subtasks" => Create<ChatSubtasksWidget, IChatSubtaskStatisticsCalculator, SubtaskStatistics>(
            c => c.Calculate(It.IsAny<ChatTokenUsage?>(), It.IsAny<TurnTokenUsage?>(), It.IsAny<IReadOnlyList<ChatMessageView>>(), It.IsAny<bool>(), It.IsAny<ChatWidgetScope>()),
            SubtaskStatistics.Empty),
        "Timeline" => Create<ChatTimelineWidget, IChatTimelineStatisticsCalculator, ChatTimelineStatistics>(
            c => c.Calculate(It.IsAny<IReadOnlyList<ChatMessageView>>(), It.IsAny<ChatTokenUsage?>(), It.IsAny<TurnTokenUsage?>(), It.IsAny<bool>()),
            ChatTimelineStatistics.Empty),
        "Branches" => Create<ChatBranchesWidget, IChatBranchesStatisticsCalculator, ChatBranchesStatistics>(
            c => c.Calculate(It.IsAny<IReadOnlyList<ChatBranchView>?>(), It.IsAny<IReadOnlyList<ChatMessageView>>(), It.IsAny<Guid?>()),
            ChatBranchesStatistics.Empty),
        _ => throw new ArgumentOutOfRangeException(nameof(widget))
    };

    private static (ComponentBase Component, Mock Calculator) Create<TComponent, TCalculator, TStatistics>(
        Expression<Func<TCalculator, TStatistics>> calculation, TStatistics empty)
        where TComponent : ComponentBase, new()
        where TCalculator : class
    {
        var calculator = new Mock<TCalculator>(MockBehavior.Strict);
        calculator.Setup(calculation).Returns(empty);
        var component = new TComponent();
        typeof(TComponent).GetProperty("Calculator", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, calculator.Object);
        return (component, calculator);
    }
}
