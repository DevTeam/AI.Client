namespace AI.Contracts.Tools;

using System.Text.Json;

/// <summary>
/// Turns one tool invocation into the semantic model the UI renders. Markup decides how a
/// presentation looks; an adapter decides what it says.
/// </summary>
/// <remarks>
/// An adapter is a pure function of the invocation's persisted data, so a row renders the same way
/// mid-run and after a restart. It must tolerate arguments and results that do not match the shape
/// it expects: both can come from a third-party server or a hand-edited chat file. The generic
/// adapter is the floor — anything an adapter cannot explain falls back to it rather than failing.
/// </remarks>
public interface IToolPresentationAdapter
{
    bool CanHandle(ToolRef tool);

    ToolCallPresentation DescribeCall(ToolRef tool, JsonElement? arguments);

    ToolResultPresentation DescribeResult(ToolRef tool, JsonElement? arguments, ToolCallResult result);
}
