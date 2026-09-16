namespace AI.Client.Application.Tests.Tools;

using System.Text.Json;
using AI.Client.Application.Tools;
using AI.Client.Contracts.Tools;
using Shouldly;
using Xunit;

public class ToolUserInterfaceTests
{
    private static ToolDescriptor WithMeta(string? meta) => new(
        "mcp_other__edit_files", "edit_files", null, null,
        JsonDocument.Parse("{}").RootElement.Clone(), null, null, [],
        meta is null ? null : JsonDocument.Parse(meta).RootElement.Clone());

    [Fact]
    public void ShouldReadTheDeclaredUiResource()
    {
        var tool = WithMeta("""{"ui":{"resourceUri":"ui://filesystem/changes"}}""");

        ToolUserInterface.ResourceUri(tool).ShouldBe("ui://filesystem/changes");
        ToolUserInterface.DeclaresApp(tool).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{}""")]
    [InlineData("""{"ui":"not an object"}""")]
    [InlineData("""{"ui":{}}""")]
    [InlineData("""{"ui":{"resourceUri":42}}""")]
    [InlineData("""{"ui":{"resourceUri":"  "}}""")]
    public void ShouldTreatAnythingElseAsNoDeclaration(string? meta)
    {
        ToolUserInterface.DeclaresApp(WithMeta(meta)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("https://evil.example/panel.html")]
    [InlineData("file:///C:/Windows/System32/drivers/etc/hosts")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public void ShouldRefuseAnythingButTheUiScheme(string uri)
    {
        // The field names a resource to fetch from the declaring server, not a URL to load. A
        // server that puts something else there gets nothing, not a navigation.
        var tool = WithMeta(JsonSerializer.Serialize(new { ui = new { resourceUri = uri } }));

        ToolUserInterface.ResourceUri(tool).ShouldBeNull();
    }

    [Fact]
    public void ShouldNotClaimToRenderAppsInThisBuild()
    {
        // Rendering a server-supplied view is only safe with the extension's full contract —
        // sandboxed frame, deny-by-default CSP, AppBridge, size lifecycle — which the current MCP
        // SDK does not provide. Declaring support without it would be the security hole.
        ToolUserInterface.IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void ShouldStillDescribeAToolThatDeclaresAnAppNatively()
    {
        // A declaration must never cost a tool its ordinary row: the native description is what
        // the user actually sees, today and whenever a server's view fails to load.
        var described = Shipped.DescribeCall("mcp_other__edit_files", """{"path":"/src/a.cs"}""");

        described.Title.ShouldBe("Edit files");
        described.Detail.ShouldBe("/src/a.cs");
    }
}
