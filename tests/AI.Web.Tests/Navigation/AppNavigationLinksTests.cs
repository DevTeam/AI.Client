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
