namespace AI.Infrastructure.Tests.Tools;

using AI.Application.Tools;
using AI.Application.Users;
using AI.Domain.Users;
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
    public async Task ShouldPreserveToolErrorsAndReportMalformedSuccessfulResults(bool isError, bool structured)
    {
        var reply = new AppToolReply();
        var response = new CallToolResult
        {
            IsError = isError,
            Content = [new TextContentBlock { Text = "Invalid document: path belongs inside properties." }],
            StructuredContent = structured ? JsonSerializer.SerializeToElement(new { unexpected = true }) : null
        };
        var users = new AmbientUserContext();
        using var scope = users.Enter(UserId.Local);
        var factory = Factory(new ProbeTool(response), reply, users);
        await using var session = await factory.OpenAsync([],
            new ToolRunContext(Guid.Empty, Guid.Empty, Guid.Empty, true), TestContext.Current.CancellationToken);
        var tool = session.Tools.Single();

        var result = await session.CallAsync(tool, "{}", null, TestContext.Current.CancellationToken);

        result.IsError.ShouldBeTrue();
        result.ModelContent.ShouldContain("path belongs inside properties");
        if (isError)
            result.StructuredContent.HasValue.ShouldBe(structured);
        else
        {
            // A malformed success is reported to the model rather than ending the turn.
            result.ModelContent.ShouldContain("does not match its declared output schema");
            result.ModelContent.ShouldContain(structured ? "the result:" : "no structured content");
            result.StructuredContent.ShouldBeNull();
        }
    }

    [Fact]
    public async Task ShouldReturnRemoteProtocolErrorsAsToolErrors()
    {
        var reply = new AppToolReply();
        var users = new AmbientUserContext();
        using var scope = users.Enter(UserId.Local);
        var factory = Factory(new RejectingTool(), reply, users);
        await using var session = await factory.OpenAsync([],
            new ToolRunContext(Guid.Empty, Guid.Empty, Guid.Empty, true), TestContext.Current.CancellationToken);

        var result = await session.CallAsync(session.Tools.Single(), "{}", null, TestContext.Current.CancellationToken);

        result.IsError.ShouldBeTrue();
        result.ModelContent.ShouldContain("Job not found: AI/code-ai/nik/IDEAI-606");
    }

    [Fact]
    public async Task ToolCallsActForTheUserWhoOpenedTheSession()
    {
        var token = TestContext.Current.CancellationToken;
        var users = new AmbientUserContext();
        var other = new UserId(Guid.Parse("019f0000-0000-7000-8000-000000000001"));
        var factory = Factory(new WhoAmITool(users), new AppToolReply(), users);
        IToolSession session;
        using (users.Enter(other))
            session = await factory.OpenAsync([], new ToolRunContext(Guid.Empty, Guid.Empty, Guid.Empty, true), token);
        await using var opened = session;

        // Called from another user's flow: the session still acts for the one who opened it.
        using var scope = users.Enter(UserId.Local);
        var result = await session.CallAsync(session.Tools.Single(), "{}", null, token);

        result.IsError.ShouldBeFalse();
        result.ModelContent.ShouldContain(other.ToString());
    }

    private static AppToolSessionFactory Factory(IAppTool tool, AppToolReply reply, AmbientUserContext users) =>
        new(new AppMcpServerHost([tool], reply), new ToolResultModelProjector(), users, users);

    private sealed class WhoAmITool(IUserContext users) : IAppTool
    {
        public McpServerTool Create(ToolRunContext run, IAppToolReply reply) =>
            McpServerTool.Create(Execute, new McpServerToolCreateOptions { SerializerOptions = reply.Json });

        [McpServerTool(Name = "who_am_i")]
        private string Execute() => users.Current.ToString();
    }

    private sealed class RejectingTool : IAppTool
    {
        public McpServerTool Create(ToolRunContext run, IAppToolReply reply) =>
            McpServerTool.Create(Execute, new McpServerToolCreateOptions { SerializerOptions = reply.Json });

        [McpServerTool(Name = "reject")]
        private string Execute() =>
            throw new ModelContextProtocol.McpProtocolException(
                "Job not found: AI/code-ai/nik/IDEAI-606", ModelContextProtocol.McpErrorCode.InvalidParams);
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
