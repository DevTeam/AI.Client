namespace AI.Infrastructure.Tests.Tools;

using AI.Application.Tools;
using AI.Contracts.Tools;
using AI.Infrastructure.Tools;
using AI.Mcp.App;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Shouldly;
using System.Text.Json;
using Xunit;

[Trait("Category", "Integration")]
public sealed class McpToolSessionTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task ShouldPreserveToolErrorsAndValidateSuccessfulResults(bool isError, bool structured)
    {
        var reply = new AppToolReply();
        var response = new CallToolResult
        {
            IsError = isError,
            Content = [new TextContentBlock { Text = "Invalid document: path belongs inside properties." }],
            StructuredContent = structured ? JsonSerializer.SerializeToElement(new { unexpected = true }) : null
        };
        var factory = new AppToolSessionFactory(new AppMcpServerHost([new ProbeTool(response)], reply),
            new ToolResultModelProjector());
        await using var session = await factory.OpenAsync([],
            new ToolRunContext(Guid.Empty, Guid.Empty, Guid.Empty, true), TestContext.Current.CancellationToken);
        var tool = session.Tools.Single();

        if (isError)
        {
            var result = await session.CallAsync(tool, "{}", null, TestContext.Current.CancellationToken);
            result.IsError.ShouldBeTrue();
            result.ModelContent.ShouldContain("path belongs inside properties");
            result.StructuredContent.HasValue.ShouldBe(structured);
        }
        else
        {
            var error = await Should.ThrowAsync<InvalidOperationException>(() =>
                session.CallAsync(tool, "{}", null, TestContext.Current.CancellationToken));
            error.Message.ShouldContain("output schema");
        }
    }

    private sealed class ProbeTool(CallToolResult result) : IAppTool
    {
        public McpServerTool Create(ToolRunContext run, IAppToolReply reply) =>
            McpServerTool.Create(Execute, new McpServerToolCreateOptions { SerializerOptions = reply.Json });

        [McpServerTool(Name = "probe", UseStructuredContent = true, OutputSchemaType = typeof(ProbeResult))]
        private CallToolResult Execute() => result;
    }

    private sealed record ProbeResult(string Value);
}
