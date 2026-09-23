namespace AI.Client.Application.Tests.Tools;

using System.Text.Json;
using AI.Client.Application.Tools;
using AI.Client.Application.Chat;
using Shouldly;
using Xunit;

public sealed class ToolCatalogRegistryTests
{
    [Fact]
    public void ShouldSearchPinAndRemoveOnlyTheCurrentRunCatalog()
    {
        var registry = new ToolCatalogRegistry();
        var run = new ToolRunContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);
        using (registry.Begin(run))
        {
            registry.Update(run, [Tool("github_issue_get", "Read GitHub issue"), Tool("file_write", "Write file")]);

            var matches = registry.SearchAndPin(run, "GitHub issue", 5);

            matches.ShouldHaveSingleItem().Name.ShouldBe("github_issue_get");
            registry.GetPinned(run).ShouldContain("github_issue_get");
        }

        registry.SearchAndPin(run, "GitHub", 5).ShouldBeEmpty();
        registry.GetPinned(run).ShouldBeEmpty();
    }

    private static AgentTool Tool(string name, string description)
    {
        var schema = JsonDocument.Parse("{}" ).RootElement.Clone();
        return new AgentTool(new ChatToolDefinition(name, description, schema),
            ToolDescriptor.Basic(name, name, description, schema), Guid.NewGuid(), name, name);
    }
}
