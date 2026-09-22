namespace AI.Client.Application.Tests.Tools;

using System.Text.Json;
using AI.Client.Application.Chat;
using AI.Client.Application.Tools;
using AI.Client.Contracts.Chat;
using AI.Client.Contracts.Settings;
using AI.Client.Contracts.Tools;
using Shouldly;
using Xunit;

public sealed class ToolDefinitionSelectorTests
{
    private static readonly string[] RequiredAppTools =
        ["ask_user", "tool_search", "context_compact", "finish_run", "app_read", "app_projects", "app_security", "spawn_subtask"];
    private readonly ContextTokenEstimator _estimator = new();

    [Fact]
    public void ShouldKeepSchemasInsideBudgetAndPreferRelevantTools()
    {
        var tools = Enumerable.Range(0, 20).Select(index =>
            Tool($"tool_{index}", "generic operation " + new string('x', 1_000))).Append(
            Tool("github_issue_get", "Read a GitHub issue and repository problem " + new string('x', 1_000))).ToArray();

        var selection = Selector().Choose(null, "Investigate this GitHub repository issue", [], tools);

        selection.AvailableCount.ShouldBe(21);
        selection.AvailableTokens.ShouldBeGreaterThan(selection.BudgetTokens);
        selection.SelectedTokens.ShouldBeLessThanOrEqualTo(selection.BudgetTokens);
        selection.Tools.ShouldContain(item => item.OriginalName == "github_issue_get");
        selection.Tools.Count.ShouldBeLessThan(tools.Length);
    }

    [Fact]
    public void ShouldKeepToolsAlreadyUsedByTheCurrentTurnAndAskUser()
    {
        var used = Tool("large_previous_tool", new string('x', 14_000));
        var ask = Tool("ask_user", new string('x', 2_000), ToolRef.AppPrefix + "ask_user");
        var others = Enumerable.Range(0, 20).Select(index => Tool($"other_{index}", new string('x', 1_000)));
        var context = new ChatCompletionMessage[]
        {
            new("user", "continue"),
            new("assistant", "", [new ChatToolCall("call-1", used.ModelDefinition.Name, "{}")]),
            new("tool", "done", ToolCallId: "call-1")
        };

        var selection = Selector().Choose(null, "continue", context, [used, ask, .. others]);

        selection.Tools.ShouldContain(item => item.OriginalName == "large_previous_tool");
        selection.Tools.ShouldContain(item => item.OriginalName == "ask_user");
    }

    private ToolDefinitionSelector Selector() => new(_estimator, new ConnectionContextLimitsResolver(), new ToolSelectionPriorityPolicy());

    [Fact]
    public void ShouldAlwaysIncludePrimaryAppCapabilities()
    {
        var required = RequiredAppTools.Select(name => Tool(name, new string('x', 200),
            name == "finish_run" ? "app_finish_run" : ToolRef.AppPrefix + name));
        var smaller = Enumerable.Range(0, 30).Select(index =>
            Tool($"small_{index}", new string('x', 2_000), ToolRef.BuiltInPrefix + $"small_{index}"));

        var selection = Selector().Choose(null, "unrelated request", [], [.. required, .. smaller]);

        selection.Tools.Select(item => item.OriginalName).ShouldContain("app_projects");
        selection.Tools.Select(item => item.OriginalName).ShouldContain("app_security");
        selection.Tools.Select(item => item.OriginalName).ShouldContain("spawn_subtask");
        selection.Tools.Select(item => item.OriginalName).ShouldContain("app_read");
        selection.Tools.Select(item => item.OriginalName).ShouldContain("ask_user");
        selection.Tools.Select(item => item.OriginalName).ShouldContain("tool_search");
        selection.Tools.Select(item => item.OriginalName).ShouldContain("context_compact");
        selection.Tools.Select(item => item.OriginalName).ShouldContain("finish_run");
    }

    [Theory]
    [InlineData("app_chats")]
    [InlineData("app_runs")]
    public void ShouldPreferRemainingAppCapabilitiesWhenRelevanceIsEqual(string name)
    {
        var preferred = Tool(name, new string('x', 2_000), ToolRef.AppPrefix + name);
        var ordinary = Enumerable.Range(0, 20).Select(index =>
            Tool($"ordinary_{index}", new string('x', 2_000), ToolRef.BuiltInPrefix + $"ordinary_{index}"));

        var selection = Selector().Choose(null, "unrelated request", [],
            [.. ordinary, preferred]);

        selection.Tools.ShouldContain(item => item.OriginalName == name);
    }

    private static AgentTool Tool(string name, string description, string? providerName = null)
    {
        var schema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();
        var definition = new ChatToolDefinition(providerName ?? name, description, schema);
        return new AgentTool(definition, ToolDescriptor.Basic(name, name, description, schema),
            Guid.NewGuid(), name, name);
    }
}
