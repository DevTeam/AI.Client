namespace AI.Client.Application.Tests.Tools;

using AI.Client.Contracts.Tools;

/// <summary>
/// The adapter set the Host ships, wired by hand the way the composition wires it. Tests describe
/// invocations through this rather than reaching for a global, so the set under test is the one the
/// containers actually register.
/// </summary>
internal static class Shipped
{
    private static ToolPresentations Instance { get; } = new(
    [
        new FileToolPresentationAdapter(),
        new ProcessToolPresentationAdapter(),
        new WebToolPresentationAdapter(),
        new AppReadPresentationAdapter(),
        new AppWritePresentationAdapter(),
        new AppSubtaskPresentationAdapter(),
    ]);

    public static ToolCallPresentation DescribeCall(string callName, string? arguments) =>
        Instance.DescribeCall(callName, arguments);

    public static ToolResultPresentation DescribeResult(string callName, string? arguments, ToolCallResult result) =>
        Instance.DescribeResult(callName, arguments, result);
}
