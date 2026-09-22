namespace AI.Client.Application.Tests.Tools;

using System.Text.Json;
using AI.Client.Application.Chat;
using AI.Client.Application.Tools;
using AI.Client.Contracts.Chat;
using AI.Client.Contracts.Tools;
using Shouldly;
using Xunit;

public sealed class ToolSearchDefinitionEnricherTests
{
    [Fact]
    public void ShouldAddAllOmittedPermittedNamesWithoutChangingTheExecutionDescriptor()
    {
        var search = Tool(ToolRef.AppPrefix + "tool_search", "tool_search", "Find omitted tools");
        var selected = Tool(ToolRef.BuiltInPrefix + "fetch", "fetch", "Fetch URL");
        var omitted = Enumerable.Range(0, 100)
            .Select(index => Tool($"{ToolRef.BuiltInPrefix}tool_{index:000}", $"tool_{index:000}", "Operation"))
            .ToArray();

        var result = new ToolSearchDefinitionEnricher(new ContextTokenEstimator())
            .Enrich([search, selected], [search, selected, .. omitted], 6_000);

        var enriched = result.Single(item => item.OriginalName == "tool_search");
        foreach (var tool in omitted)
            enriched.ModelDefinition.Description.ShouldContain(tool.ModelDefinition.Name);
        enriched.Descriptor.ShouldBeSameAs(search.Descriptor);
        search.ModelDefinition.Description.ShouldBe("Find omitted tools");
    }

    [Fact]
    public void ShouldNotListToolsOutsideThePermittedInput()
    {
        var search = Tool(ToolRef.AppPrefix + "tool_search", "tool_search", "Find omitted tools");
        var permitted = Tool(ToolRef.BuiltInPrefix + "read_text_file", "read_text_file", "Read file");
        var denied = Tool(ToolRef.BuiltInPrefix + "delete_file", "delete_file", "Delete file");

        var result = new ToolSearchDefinitionEnricher(new ContextTokenEstimator())
            .Enrich([search], [search, permitted], 6_000);

        var description = result.Single().ModelDefinition.Description;
        description.ShouldContain(permitted.ModelDefinition.Name);
        description.ShouldNotContain(denied.ModelDefinition.Name);
    }

    private static AgentTool Tool(string callName, string originalName, string description)
    {
        var schema = JsonDocument.Parse("{}" ).RootElement.Clone();
        var definition = new ChatToolDefinition(callName, description, schema);
        return new AgentTool(definition, ToolDescriptor.Basic(callName, originalName, description, schema),
            Guid.NewGuid(), originalName, originalName);
    }
}
