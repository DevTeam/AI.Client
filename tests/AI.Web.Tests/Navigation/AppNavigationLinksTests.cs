namespace AI.Web.Tests.Navigation;

using AI.Contracts.Navigation;
using AI.Web.Navigation;
using Shouldly;
using Xunit;

public class AppNavigationLinksTests
{
    private readonly AppNavigationLinks _links = new(new AppNavigationTargets());
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly Guid Chat = Guid.NewGuid();
    private static readonly Guid Branch = Guid.NewGuid();

    [Fact]
    public void ShouldSelectSkillsAndToolsWithoutExecutingThem()
    {
        var skill = _links.Parse($"aiclient://navigate/settings.skills?skillId=my%20skill&projectId={Project}", null);
        skill.ShouldNotBeNull().SkillId.ShouldBe("my skill");
        skill.ProjectId.ShouldBe(Project);
        skill.Action.ShouldBe("show");
        var tool = _links.Parse("aiclient://navigate/settings.tools?toolName=mcp_csharp__cs_run", null);
        tool.ShouldNotBeNull().ToolName.ShouldBe("mcp_csharp__cs_run");
        tool.Action.ShouldBe("show");
    }

    [Theory]
    [InlineData("settings.skills?skillId=")]
    [InlineData("settings.skills?skillId=a&skillId=b")]
    [InlineData("settings.tools?skillId=a")]
    [InlineData("settings.skills?toolName=a")]
    [InlineData("settings.tools?toolName=%0A")]
    [InlineData("settings.tools?toolName=a&action=click")]
    public void ShouldRejectInvalidCatalogLinks(string destination) =>
        _links.Parse("aiclient://navigate/" + destination, Project).ShouldBeNull();

    [Theory]
    [InlineData("settings.skills?skillId=skill-create", "mention-skill")]
    [InlineData("settings.tools?toolName=mcp_csharp__cs_run", "mention-tool")]
    public void ShouldRenderCatalogLinksWithSharedIconClasses(string destination, string iconClass)
    {
        var renderer = new AI.Web.Markdown.SafeMarkdownRenderer();
        var markdown = $"[Name](aiclient://navigate/{destination})";
        foreach (var html in new[] { renderer.Render(markdown), renderer.RenderInline(markdown) })
        {
            html.ShouldContain($"class=\"mention-link {iconClass}\"");
            html.ShouldContain($"href=\"aiclient://navigate/{destination}\"");
        }
        renderer.Render($"`{markdown}`").ShouldNotContain("<a ");
    }

    [Fact]
    public void ShouldPreserveApplicationLinksInRenderedMessages()
    {
        var html = new AI.Web.Markdown.SafeMarkdownRenderer().Render("[Settings](aiclient://navigate/settings)");
        html.ShouldContain("href=\"aiclient://navigate/settings\"");
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("settings.connections")]
    [InlineData("settings.tools")]
    [InlineData("settings.skills")]
    [InlineData("settings.memory")]
    [InlineData("widgets.chat-tools")]
    [InlineData("chat.context")]
    [InlineData("chat.composer")]
    [InlineData("chat.text_correction")]
    [InlineData("chat.branches")]
    [InlineData("chat.widgets")]
    [InlineData("chat.send")]
    [InlineData("chat.fork")]
    [InlineData("chat.edit_branch")]
    [InlineData("chat.new")]
    [InlineData("projects.create")]
    [InlineData("workspace.sidebar.toggle")]
    [InlineData("workspace.notifications")]
    public void ShouldRevealPanelsWithoutActivatingControls(string target)
    {
        var result = _links.Parse($"aiclient://navigate/{target}", Project);
        result.ShouldNotBeNull().Target.ShouldBe(target);
        result.Action.ShouldBe("show");
        result.WaitForContinue.ShouldBeFalse();
        result.Value.ShouldBeNull();
    }

    [Fact]
    public void ShouldNavigateToAnExplicitBranch()
    {
        var result = _links.Parse($"aiclient://navigate/branch?projectId={Project}&chatId={Chat}&branchId={Branch}", null);
        result.ShouldNotBeNull().Action.ShouldBe("click");
        result.ProjectId.ShouldBe(Project);
        result.ChatId.ShouldBe(Chat);
        result.BranchId.ShouldBe(Branch);
    }

    [Fact]
    public void ShouldOpenGlobalSettingsBeforeAProjectExists()
    {
        _links.Parse("aiclient://navigate/settings", null).ShouldNotBeNull();
        _links.Parse("aiclient://navigate/widgets", null).ShouldBeNull();
        _links.Parse("aiclient://navigate/project.settings", null).ShouldBeNull();
        _links.Parse("aiclient://navigate/chat.context", null).ShouldBeNull();
    }

    [Fact]
    public void ShouldRevealContextInTheSpecifiedChat()
    {
        var result = _links.Parse($"aiclient://navigate/chat.context?projectId={Project}&chatId={Chat}", null);
        result.ShouldNotBeNull().Target.ShouldBe("chat.context");
        result.ProjectId.ShouldBe(Project);
        result.ChatId.ShouldBe(Chat);
        result.Action.ShouldBe("show");
    }

    [Theory]
    [InlineData("aiclient://navigate/chat")]
    [InlineData("aiclient://navigate/chat.demo")]
    [InlineData("aiclient://navigate/settings?action=set_value")]
    [InlineData("aiclient://navigate/settings?value=secret")]
    [InlineData("aiclient://navigate/settings?projectId=invalid")]
    [InlineData("aiclient://navigate/settings?projectId=00000000-0000-0000-0000-000000000000")]
    [InlineData("aiclient://navigate/settings#fragment")]
    [InlineData("aiclient://navigate/../settings")]
    [InlineData("aiclient://user@navigate/settings")]
    [InlineData("https://navigate/settings")]
    [InlineData("aiclient://navigate/unknown")]
    public void ShouldRejectInvalidAndMutatingLinks(string href) => _links.Parse(href, Project).ShouldBeNull();

    [Fact]
    public void ShouldRejectDuplicateAndIncompleteIds()
    {
        _links.Parse($"aiclient://navigate/project?projectId={Project}&projectId={Project}", null).ShouldBeNull();
        _links.Parse($"aiclient://navigate/branch?projectId={Project}&chatId={Chat}", null).ShouldBeNull();
        _links.Parse($"aiclient://navigate/chat?chatId={Chat}", Project).ShouldBeNull();
    }
}
