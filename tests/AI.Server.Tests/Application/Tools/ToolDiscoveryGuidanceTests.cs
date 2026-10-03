namespace AI.Application.Tests.Tools;

using System.Text.Json;
using AI.Application.Chat;
using AI.Application.Tools;
using AI.Contracts.Tools;
using Shouldly;
using Xunit;

public sealed class ToolDiscoveryGuidanceTests
{
    private readonly ToolDiscoveryGuidance _guidance = new();

    [Fact]
    public void ShouldReferenceTheActualOfferedSearchNameAndItsArguments()
    {
        var search = Tool(ToolRef.ToolSearchName, "tool_search");
        var text = _guidance.ForSelection(new([search], 10, 500, 50, 100)).ShouldNotBeNull();
        text.ShouldContain("call " + search.ModelDefinition.Name);
        text.ShouldContain("query");
        text.ShouldContain("limit=1");
        text.ShouldContain("earlier messages and summaries may be stale");
        text.ShouldNotContain("call app_tool_search");
    }

    [Theory]
    [InlineData("mcp_built_in__read_file", false)]
    [InlineData("mcp_built_in__process_run", true)]
    public void ShouldRecoverFromUnknownAndOmittedNamesWithoutExecutingAnAlias(string name, bool omitted)
    {
        var search = Tool(ToolRef.ToolSearchName, "tool_search");
        var process = Tool("mcp_built_in__process_run", "process_run");
        var text = _guidance.ForUnavailableCall(name, [search], [search, process]);
        text.ShouldContain(omitted ? "schema was omitted" : "There is no tool named");
        text.ShouldContain("call " + ToolRef.ToolSearchName);
        text.ShouldContain("only after its definition is offered");
        text.ShouldNotContain("call app_tool_search");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldNotSuggestDiscoveryWhenItsSchemaIsMissingEvenIfItIsPermitted(bool permittedSearch)
    {
        var process = Tool("mcp_built_in__process_run", "process_run");
        var search = Tool(ToolRef.ToolSearchName, "tool_search");
        var permitted = permittedSearch ? new[] { process, search } : [process];
        var error = _guidance.ForUnavailableCall("mcp_built_in__read_file", [process], permitted);
        error.ShouldContain("No tool-discovery tool is offered");
        error.ShouldNotContain(ToolRef.ToolSearchName);
        var selection = _guidance.ForSelection(new([process], 10, 500, 50, 100)).ShouldNotBeNull();
        selection.ShouldNotContain(ToolRef.ToolSearchName);
    }

    [Fact]
    public void ShouldDropGuidanceWhenNoToolsAreOffered() =>
        _guidance.ForSelection(new([], 0, 0, 0, 0)).ShouldBeNull();

    [Fact]
    public void ShouldKeepCurrentNamesAuthoritativeEvenWhenTheFullCatalogueFits()
    {
        var tool = Tool("mcp_built_in__process_run", "process_run");
        var text = _guidance.ForSelection(new([tool], 1, 50, 50, 100)).ShouldNotBeNull();
        text.ShouldContain("Names in earlier messages and summaries may be stale");
        text.ShouldNotContain("permitted tools are shown");
        text.ShouldNotContain("call " + ToolRef.ToolSearchName);
    }

    private static AgentTool Tool(string name, string original)
    {
        var schema = JsonDocument.Parse("{}").RootElement.Clone();
        return new(new(name, original, schema), ToolDescriptor.Basic(name, original, original, schema),
            Guid.NewGuid(), original, "schema");
    }
}
