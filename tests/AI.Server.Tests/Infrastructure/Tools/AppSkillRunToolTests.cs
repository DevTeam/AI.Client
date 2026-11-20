namespace AI.Server.Tests.Infrastructure.Tools;

using System.Text.Json;
using AI.Application.Chat;
using AI.Application.Tools;
using AI.Mcp.App;
using Shouldly;
using Xunit;

public sealed class AppSkillRunToolTests
{
    [Fact]
    public void ShouldHandThePlaybookTheNamesItsToolsAreCalledByInThisRun()
    {
        var output = JsonElement.Parse(
            """{"kind":"playbook","instructions":"Use 1C_EditMetadata.","tools":["1C_EditMetadata","app_read","missing"]}""");
        const string server = "mcp_8f2a4c6e1b3d4a7f9c526e0d4b1f8a37__";

        var result = AppSkillRunTool.WithCallNames(output, ["1C_EditMetadata", "app_read", "missing"],
            [server + "1C_EditMetadata", "mcp_app__app_read"]);

        result.GetProperty("tools").EnumerateArray().Select(tool => tool.GetString())
            .ShouldBe([server + "1C_EditMetadata", "mcp_app__app_read", "missing"]);
        result.GetProperty("instructions").GetString().ShouldBe("Use 1C_EditMetadata.");
    }

    [Fact]
    public void ShouldPinAToolOfAnyServerByItsBareName()
    {
        var registry = new ToolCatalogRegistry();
        var run = new ToolRunContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);
        using var scope = registry.Begin(run);
        const string name = "mcp_0123456789abcdef0123456789abcdef__1C_EditMetadata";
        registry.Update(run, [Tool(name)]);

        registry.Pin(run, ["1C_EditMetadata"]).ShouldBe([name]);
    }

    private static AgentTool Tool(string name)
    {
        var schema = JsonElement.Parse("{}");
        return new AgentTool(new ChatToolDefinition(name, "Edits metadata", schema),
            ToolDescriptor.Basic(name, name, "Edits metadata", schema), Guid.NewGuid(), name, name);
    }
}
