namespace AI.Application.Tests.Tools;

using System.Text.Json;
using AI.Application.Tools;
using AI.Application.Chat;
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

            var matches = registry.SearchAndPin(run, "GitHub issue", 5).Tools;

            matches.ShouldHaveSingleItem().Name.ShouldBe("github_issue_get");
            registry.GetPinned(run).ShouldContain("github_issue_get");
        }

        registry.SearchAndPin(run, "GitHub", 5).Tools.ShouldBeEmpty();
        registry.GetPinned(run).ShouldBeEmpty();
    }

    [Fact]
    public void ShouldMatchPlainFormsAndRankNamesAboveDescriptions()
    {
        var registry = new ToolCatalogRegistry();
        var run = new ToolRunContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);
        using var scope = registry.Begin(run);
        registry.Update(run, [Tool("mcp_fs__read_text_file", "Reads a text file"),
            Tool("mcp_fs__thread_list", "Lists the threads already running"),
            Tool("mcp_fs__file_info", "Gets metadata; can read the size of files")]);

        var found = registry.SearchAndPin(run, "read files.", 5).Tools.Select(match => match.Name).ToArray();

        // "files" finds "file", and "read" is not found inside "thread" or "already".
        found.ShouldBe(["mcp_fs__read_text_file", "mcp_fs__file_info"]);
    }

    [Fact]
    public void ShouldFindAToolByItsParameters()
    {
        var registry = new ToolCatalogRegistry();
        var run = new ToolRunContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);
        using var scope = registry.Begin(run);
        registry.Update(run, [Tool("mcp_web__get", "Fetches a resource",
            """{"type":"object","properties":{"url":{"type":"string","description":"The web page address"}}}"""),
            Tool("mcp_fs__list", "Lists entries")]);

        registry.SearchAndPin(run, "web page", 5).Tools.ShouldHaveSingleItem().Name.ShouldBe("mcp_web__get");
    }

    [Fact]
    public void ShouldPinOnlyTheBestMatchesAndCountTheRest()
    {
        var registry = new ToolCatalogRegistry();
        var run = new ToolRunContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);
        using var scope = registry.Begin(run);
        registry.Update(run, Enumerable.Range(0, 10).Select(index => Tool($"mcp_fs__file_{index}", "Works with a file")).ToArray());

        var found = registry.SearchAndPin(run, "file", 5);

        found.Tools.Count.ShouldBe(5);
        found.Remaining.ShouldBe(5);
        found.Tools.Count(match => match.Prioritized).ShouldBe(ToolCatalogRegistry.MaxPinned);
        registry.GetPinned(run).Count.ShouldBe(ToolCatalogRegistry.MaxPinned);
    }

    [Fact]
    public void ShouldReturnAnExactlyNamedToolAloneAndPinIt()
    {
        var registry = new ToolCatalogRegistry();
        var run = new ToolRunContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);
        using var scope = registry.Begin(run);
        registry.Update(run, [Tool("mcp_fs__file_read", "Reads a file"), Tool("mcp_fs__file_write", "Writes a file")]);

        var found = registry.SearchAndPin(run, "file_write", 5);

        found.Tools.ShouldHaveSingleItem().Name.ShouldBe("mcp_fs__file_write");
        registry.GetPinned(run).ShouldHaveSingleItem().ShouldBe("mcp_fs__file_write");
    }

    [Fact]
    public void ShouldSearchOneServerWhenAsked()
    {
        var registry = new ToolCatalogRegistry();
        var run = new ToolRunContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);
        using var scope = registry.Begin(run);
        registry.Update(run, [Tool("mcp_github__issue_get", "Reads an issue"), Tool("mcp_jira__issue_get", "Reads an issue")]);

        registry.SearchAndPin(run, "issue", 5, "jira").Tools.ShouldHaveSingleItem().Name.ShouldBe("mcp_jira__issue_get");
    }

    [Fact]
    public void ShouldRejectAQueryThatIsNotInEnglish()
    {
        var registry = new ToolCatalogRegistry();
        var run = new ToolRunContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);
        using var scope = registry.Begin(run);
        registry.Update(run, [Tool("mcp_fs__read_text_file", "Reads a text file")]);

        Should.Throw<ArgumentException>(() => registry.SearchAndPin(run, "прочитать файл", 5)).Message.ShouldContain("English");
        registry.GetPinned(run).ShouldBeEmpty();
    }

    [Fact]
    public void ShouldPinToolsNamedWithOrWithoutTheirServerPrefix()
    {
        var registry = new ToolCatalogRegistry();
        var run = new ToolRunContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);
        using var scope = registry.Begin(run);
        registry.Update(run, [Tool("mcp_app__app_chats", "Chats"), Tool("mcp_app__app_runs", "Runs"),
            Tool("mcp_app__app_chats_extra", "Other"), Tool("ask_user", "Ask")]);

        var pinned = registry.Pin(run, ["app_chats", "ask_user", "missing"]);

        pinned.ShouldBe(["mcp_app__app_chats", "ask_user"], ignoreOrder: true);
        registry.GetPinned(run).ShouldNotContain("mcp_app__app_runs");
    }

    [Fact]
    public void ShouldRecordEachPlaybookOncePerRunAndArguments()
    {
        var registry = new ToolCatalogRegistry();
        var run = new ToolRunContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);
        using (registry.Begin(run))
        {
            registry.TryRecordPlaybook(run, "directory-analyze", "{}").ShouldBeTrue();
            registry.TryRecordPlaybook(run, "directory-analyze", "{}").ShouldBeFalse();
            registry.TryRecordPlaybook(run, "directory-analyze", """{"path":"D:\\Other"}""").ShouldBeTrue();
        }

        using (registry.Begin(run))
            registry.TryRecordPlaybook(run, "directory-analyze", "{}").ShouldBeTrue();
    }

    private static AgentTool Tool(string name, string description, string schemaJson = "{}")
    {
        var schema = JsonDocument.Parse(schemaJson).RootElement.Clone();
        return new AgentTool(new ChatToolDefinition(name, description, schema),
            ToolDescriptor.Basic(name, name, description, schema), Guid.NewGuid(), name, name);
    }
}
