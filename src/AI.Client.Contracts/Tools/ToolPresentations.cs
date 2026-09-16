namespace AI.Client.Contracts.Tools;

using System.Text.Json;

/// <summary>
/// Picks the adapter for an invocation and hands back its presentation. The generic adapter always
/// answers last, so every tool — including one this build has never seen — gets a description.
/// </summary>
/// <remarks>
/// The adapters are injected rather than built here: this type only decides which one answers, and
/// the composition decides which set of them the build ships. Consumers take it as a dependency so
/// that nothing reaches for a global instance behind the container's back.
/// </remarks>
public sealed class ToolPresentations(IEnumerable<IToolPresentationAdapter> adapters)
{
    private static readonly GenericToolPresentationAdapter Generic = new();

    private IToolPresentationAdapter Select(ToolRef tool)
    {
        foreach (var adapter in adapters)
            if (adapter.CanHandle(tool))
                return adapter;
        return Generic;
    }

    public ToolCallPresentation DescribeCall(string callName, string? arguments)
    {
        var tool = ToolRef.Parse(callName);
        var input = ParseArguments(arguments);
        var adapter = Select(tool);
        try
        {
            return adapter.DescribeCall(tool, input);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            // An adapter that trips over unexpected arguments must not take the transcript with
            // it; the generic description is always derivable.
            return Generic.DescribeCall(tool, input);
        }
    }

    public ToolResultPresentation DescribeResult(string callName, string? arguments, ToolCallResult result)
    {
        var tool = ToolRef.Parse(callName);
        var input = ParseArguments(arguments);
        var adapter = Select(tool);
        try
        {
            return adapter.DescribeResult(tool, input, result);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return Generic.DescribeResult(tool, input, result);
        }
    }

    /// <summary>Arguments are model-authored text; unparseable input simply means "no detail".</summary>
    public static JsonElement? ParseArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return null;
        try
        {
            return JsonDocument.Parse(arguments).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
