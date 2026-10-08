namespace AI.Application.Tests.Tools;

using AI.Contracts.Settings;
using Shouldly;
using Xunit;

public sealed class ToolDefaultDecisionTests
{
    private readonly ToolDefaultDecision _defaults = new();

    [Theory]
    [InlineData("list_allowed_directories")]
    [InlineData("read_text_file")]
    [InlineData("read_multiple_files")]
    [InlineData("read_image_file")]
    [InlineData("list_directory")]
    [InlineData("directory_tree")]
    [InlineData("search_files")]
    [InlineData("grep_files")]
    [InlineData("get_file_info")]
    [InlineData("zip_list")]
    [InlineData("zip_read")]
    [InlineData("trigger_wait")]
    public void BuiltInReadToolsRunWithoutApproval(string name) =>
        _defaults.GetDecision(DefaultMcpServer.Id, name).ShouldBe("Allow");

    [Theory]
    [InlineData("app_read")]
    [InlineData("tool_search")]
    [InlineData("skill_search")]
    [InlineData("ask_user")]
    [InlineData("app_navigate")]
    public void AppReadAndNavigationToolsRunWithoutApproval(string name) =>
        _defaults.GetDecision(AppMcpServer.Id, name).ShouldBe("Allow");

    [Theory]
    [InlineData("write_file")]
    [InlineData("delete_directory")]
    [InlineData("process_run")]
    [InlineData("fetch")]
    public void BuiltInToolsWithSideEffectsOrNetworkAccessStillAsk(string name) =>
        _defaults.GetDecision(DefaultMcpServer.Id, name).ShouldBe("Ask");

    [Theory]
    [InlineData("app_security")]
    [InlineData("app_chats")]
    [InlineData("spawn_subtask")]
    [InlineData("run_skill")]
    public void AppMutationsAndDelegationStillAsk(string name) =>
        _defaults.GetDecision(AppMcpServer.Id, name).ShouldBe("Ask");

    [Fact]
    public void UnknownAndExternalToolsStillAsk()
    {
        _defaults.GetDecision(DefaultMcpServer.Id, "new_tool").ShouldBe("Ask");
        _defaults.GetDecision(Guid.NewGuid(), "read_text_file").ShouldBe("Ask");
        _defaults.GetDecision(CSharpMcpServer.Id, "cs_run").ShouldBe("Ask");
    }
}
