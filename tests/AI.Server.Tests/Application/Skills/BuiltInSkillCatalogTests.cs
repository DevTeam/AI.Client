namespace AI.Application.Tests.Skills;

using AI.Application.Skills;
using AI.Contracts.Skills;
using Shouldly;
using Xunit;

public class BuiltInSkillCatalogTests
{
    private static readonly string[] Domains = ["chat", "project", "memory", "skill", "instructions", "code", "git", "devops", "qa", "mermaid", "svg", "settings", "app", "team"];

    // An abbreviation keeps its official spelling in the name (docs/27-skills.md, skill-create).
    private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.Ordinal)
    {
        ["api"] = "API", ["ci"] = "CI", ["csharp"] = "C#", ["devops"] = "DevOps", ["e2e"] = "E2E",
        ["er"] = "ER", ["mcp"] = "MCP", ["qa"] = "QA", ["svg"] = "SVG", ["ui"] = "UI",
    };

    [Fact]
    public void ShouldExposeCSharpAndShellExecutionPlaybooks()
    {
        var catalog = new BuiltInSkillCatalog();
        var csharp = catalog.GetById("code-run-csharp").ShouldNotBeNull();
        csharp.Kind.ShouldBe(SkillKinds.Playbook);
        csharp.Aliases.ShouldBe(["c#", "cs", "csharp", "cs-run"]);
        csharp.AllowedTools.ShouldNotBeNull().ShouldContain("cs_run");
        var shell = catalog.GetById("code-run-shell").ShouldNotBeNull();
        shell.Kind.ShouldBe(SkillKinds.Playbook);
        shell.Aliases.ShouldNotBeNull().ShouldContain("powershell");
        shell.AllowedTools.ShouldNotBeNull().ShouldContain("process_run");
    }

    [Fact]
    public void ShouldExposeBundledChatRenameInstructions()
    {
        var catalog = new BuiltInSkillCatalog();

        var skill = catalog.GetById("chat-rename");

        skill.ShouldNotBeNull();
        skill.Source.ShouldBe("Built-in");
        skill.Kind.ShouldBe(SkillKinds.Executor);
        skill.Content.ShouldContain("read_chat");
        catalog.List().ShouldContain(item => item.Id == skill.Id);
    }

    [Fact]
    public void ShouldResolveEverySelectableGuideToABundledSkill()
    {
        var catalog = new BuiltInSkillCatalog();
        var topics = new AI.Contracts.Navigation.AppGuideTopics();
        topics.Find("permissions").ShouldNotBeNull();
        topics.Find("navigation").ShouldNotBeNull();
        topics.Find("widgets").ShouldNotBeNull();
        foreach (var topic in topics.All)
            catalog.GetById(topic.SkillId).ShouldNotBeNull(topic.Id);
    }

    [Fact]
    public void ShouldLimitEveryBundledGuideStepToFifteenSeconds()
    {
        var guides = new BuiltInSkillCatalog().List().Where(skill => skill.Id.StartsWith("app-guide-", StringComparison.Ordinal)).ToArray();
        guides.Length.ShouldBe(13);
        foreach (var guide in guides) guide.Content.Contains("`timeoutSeconds=15`", StringComparison.Ordinal).ShouldBeTrue(guide.Id);
    }

    [Fact]
    public void EveryGuideShouldDiscoverControlHelpInsteadOfKeepingItsOwnApplicationFacts()
    {
        foreach (var guide in new BuiltInSkillCatalog().List().Where(skill => skill.Id.StartsWith("app-guide-", StringComparison.Ordinal)))
        {
            guide.Content.ShouldContain("## Current control help");
            guide.Content.ShouldContain("`Hint`");
            guide.Content.ShouldContain("`UiHint`");
            guide.Content.ShouldContain("Refresh discovery after revealing a panel");
            guide.Content.ShouldContain("## Learning routes");
            guide.Content.ShouldNotContain("## Routes and application facts");
        }
    }

    [Fact]
    public void ShouldGiveEveryGuideTheSameLanguagePriorityForHiddenAndRegularChats()
    {
        var guides = new BuiltInSkillCatalog().List().Where(skill => skill.Id.StartsWith("app-guide-", StringComparison.Ordinal)).ToArray();
        guides.Length.ShouldBe(13);
        foreach (var guide in guides)
        {
            guide.Content.ShouldContain("`requestedLanguage`");
            guide.Content.ShouldContain("`lastUserMessage`");
            guide.Content.ShouldContain("`clientLocale`");
            guide.Content.ShouldContain("Preserve actual control labels verbatim");
        }
    }

    [Fact]
    public void ShouldKeepGuidesVisualAndOfferInterestChoicesWithFreeText()
    {
        var catalog = new BuiltInSkillCatalog();
        foreach (var guide in catalog.List().Where(skill => skill.Id.StartsWith("app-guide-", StringComparison.Ordinal)))
        {
            guide.ParametersSchema.GetProperty("properties").GetProperty("interest").GetProperty("type").GetString().ShouldBe("string");
            guide.Content.ShouldContain("allowOther=true");
            guide.Content.ShouldContain("blocks of 2–3 steps");
            guide.Content.ShouldContain("every substantive explanation belongs in the comment");
            guide.Content.ShouldContain("only brief progress markers");
        }
        new AI.Contracts.Navigation.AppNavigationTargets().Find("settings.connection.context_window")
            .ShouldNotBeNull().Hint.ShouldNotBeNull().ShouldContain("endpoint's real model limit");
    }

    [Fact]
    public void ShouldLetTheModelChangeAndInheritBranchSettings()
    {
        var catalog = new BuiltInSkillCatalog();
        var configure = catalog.GetById("chat-branch-configure").ShouldNotBeNull();
        configure.AllowedTools.ShouldBe(["app_read", "app_chats", "app_security", "ask_user"]);
        configure.Content.ShouldContain("SetBranchSettings");
        configure.Content.ShouldContain("effectiveBranchSettings");
        configure.Content.ShouldContain("Loosening approvals needs the person's word");
        configure.Content.ShouldContain("never inherited");

        var reset = catalog.GetById("settings-tools-reset").ShouldNotBeNull();
        reset.Content.ShouldContain("RemoveBranchToolPolicy");
        reset.ParametersSchema.GetProperty("properties").GetProperty("scope").GetProperty("enum")
            .EnumerateArray().Select(item => item.GetString()).ShouldContain("branch");
        catalog.GetById("settings-select-connection").ShouldNotBeNull().ParametersSchema.GetProperty("properties")
            .GetProperty("scope").GetProperty("enum").EnumerateArray().Select(item => item.GetString()).ShouldContain("Branch");
    }

    [Fact]
    public void ShouldOfferToDeleteTheChatOnlyFromPlaybooksThatCanDeleteIt()
    {
        var skills = new BuiltInSkillCatalog().List();

        // Offering the deletion is the last step of a playbook that changed data and reported one line.
        string[] offering =
        [
            "chat-archive", "chat-branch-cleanup", "project-rename", "project-describe", "project-directory-add",
            "project-directory-remove", "instructions-edit", "memory-save", "memory-review", "memory-forget",
            "settings-select-connection", "settings-tools-configure", "project-tools-configure",
            "chat-tools-configure", "settings-tools-reset",
        ];
        foreach (var id in offering)
        {
            var skill = skills.Single(item => item.Id == id);
            SkillMarkdown.Body(skill.Content).Contains("delete this chat", StringComparison.Ordinal).ShouldBeTrue(id);
            skill.AllowedTools.ShouldNotBeNull().ShouldContain("app_chats", id);
        }

        // Every playbook that mentions the offer declares the tool, and the safe testing example never
        // offers it: its answer is the deliverable.
        foreach (var skill in skills.Where(item => item.Kind == SkillKinds.Playbook))
            if (SkillMarkdown.Body(skill.Content).Contains("delete this chat", StringComparison.Ordinal))
                skill.AllowedTools.ShouldNotBeNull().ShouldContain("app_chats", skill.Id);
        skills.Single(item => item.Id == "chat-summary").Content.ShouldContain("never offers to delete the chat");
    }

    [Fact]
    public void ShouldFollowTheSkillNamingSystem()
    {
        foreach (var skill in new BuiltInSkillCatalog().List())
        {
            // <domain>-<action>[-<object>], and the name is the id in words with official abbreviations.
            Domains.ShouldContain(skill.Id.Split('-')[0], skill.Id);
            skill.Id.Split('-').Length.ShouldBeGreaterThan(1, skill.Id);
            var words = skill.Id.Split('-').Select(word => Abbreviations.GetValueOrDefault(word, word)).ToArray();
            words[0] = char.ToUpperInvariant(words[0][0]) + words[0][1..];
            skill.Name.ShouldBe(string.Join(' ', words), skill.Id);
            char.IsUpper(skill.Description[0]).ShouldBeTrue(skill.Id);
        }
    }

    [Fact]
    public void ShouldOfferNavigationShortcutsInEveryGuideWithoutReplacingVisualSteps()
    {
        var guides = new BuiltInSkillCatalog().List().Where(skill => skill.Id.StartsWith("app-guide-", StringComparison.Ordinal)).ToArray();
        guides.Length.ShouldBe(13);
        foreach (var guide in guides)
        {
            guide.Content.ShouldContain("aiclient://navigate/settings.connections");
            guide.Content.ShouldContain("final visible takeaway");
            guide.Content.ShouldContain("visible step comment or `ask_user` question text");
            guide.Content.ShouldContain("must not replace its tool calls");
            guide.Content.ShouldContain("Never link to the hidden guide chat");
        }
    }

    [Fact]
    public void ShouldGiveEveryBundledSkillAnIconThatSkillCreateOffers()
    {
        var skills = new BuiltInSkillCatalog().List();
        var conventions = string.Join(' ', skills.Single(skill => skill.Id == "skill-create").Content
            .ReplaceLineEndings("\n").Split('\n').Select(line => line.Trim()));

        foreach (var skill in skills) SkillIcons.Names.ShouldContain(skill.Icon, skill.Id);
        // skill-create lists the icons for the model that drafts a skill; the list must not drift.
        conventions.ShouldContain($"One of: {string.Join(", ", SkillIcons.Names)}.");
    }

    [Fact]
    public void ShouldTellEveryMermaidSkillToKeepDiagramsSmall()
    {
        var mermaid = new BuiltInSkillCatalog().List()
            .Where(skill => skill.Id.StartsWith("mermaid-", StringComparison.Ordinal)).ToArray();

        mermaid.Length.ShouldBe(14);
        foreach (var skill in mermaid)
            skill.Content.Contains("hard to read in the chat", StringComparison.Ordinal).ShouldBeTrue(skill.Id);
    }

    [Fact]
    public void ShouldGiveEveryAliasToOneSkillOnly()
    {
        var skills = new BuiltInSkillCatalog().List();
        var commands = skills.SelectMany(skill => (skill.Aliases ?? []).Append(skill.Id)).ToArray();

        commands.Distinct(StringComparer.Ordinal).Count().ShouldBe(commands.Length);
        skills.Single(skill => skill.Id == "chat-context-compact").Aliases.ShouldBe(["compact"]);
    }

    [Fact]
    public void ShouldMakeEveryBundledSkillExceptTheExecutorsAPlaybookWithDeclaredTools()
    {
        var skills = new BuiltInSkillCatalog().List();
        string[] executors = ["chat-rename", "chat-reply-suggest", "skill-route", "chat-tool-risk-assess", "chat-comment-suggest"];

        skills.Count.ShouldBeGreaterThanOrEqualTo(26);
        skills.Where(item => executors.Contains(item.Id)).ShouldAllBe(item => item.Kind == SkillKinds.Executor);
        foreach (var skill in skills.Where(item => !executors.Contains(item.Id)))
        {
            skill.Kind.ShouldBe(SkillKinds.Playbook, skill.Id);
            skill.ResultSchema.ShouldBeNull(skill.Id);
            skill.AllowedTools.ShouldNotBeNull().ShouldNotBeEmpty(skill.Id);
            // Every tool a playbook names in its steps is one it declares.
            foreach (var tool in new[] { "app_projects", "app_security", "app_chats", "app_runs", "app_memory",
                         "app_skills", "app_instructions", "app_navigate", "ask_user", "context_compact" })
                if (SkillMarkdown.Body(skill.Content).Contains($"`{tool}`", StringComparison.Ordinal))
                    skill.AllowedTools.ShouldContain(tool, skill.Id);
        }
    }
}
