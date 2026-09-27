namespace AI.Contracts.Tools;

public interface IToolPresentations
{
    ToolCallPresentation DescribeCall(string callName, string? arguments);

    ToolResultPresentation DescribeResult(string callName, string? arguments, ToolCallResult result);
}