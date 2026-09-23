namespace AI.Client.Application.Tests.Tools;

using AI.Client.Contracts.Tools;

/// <summary>
/// The adapter set the Host ships, resolved from the shared composition setup. Tests describe
/// invocations through this rather than wiring adapters by hand, so the set under test is the one
/// the containers actually register — including its order and fallback.
/// </summary>
internal static class Shipped
{
    private static readonly ToolsComposition Composition = new();

    public static IToolResultModelProjector ModelProjector => Composition.ModelProjector;

    public static IToolResultCodec ToolResultCodec => Composition.Codec;

    public static ToolCallPresentation DescribeCall(string callName, string? arguments) =>
        Composition.Presentations.DescribeCall(callName, arguments);

    public static ToolResultPresentation DescribeResult(string callName, string? arguments, ToolCallResult result) =>
        Composition.Presentations.DescribeResult(callName, arguments, result);
}
