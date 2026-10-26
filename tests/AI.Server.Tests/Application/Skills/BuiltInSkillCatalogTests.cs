namespace AI.Application.Tests.Skills;

using AI.Application.Skills;
using AI.Contracts.Skills;
using Shouldly;
using Xunit;

public class BuiltInSkillCatalogTests
{
    private static readonly string[] Domains = ["chat", "project", "memory", "skill", "instructions", "code", "git", "devops", "qa", "debug", "mermaid", "svg", "settings", "mcp", "app", "team"];

    // An abbreviation keeps its official spelling in the name (docs/27-skills.md, skill-create).
    private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.Ordinal)
    {
        ["api"] = "API", ["ci"] = "CI", ["csharp"] = "C#", ["devops"] = "DevOps", ["e2e"] = "E2E",
        ["er"] = "ER", ["mcp"] = "MCP", ["qa"] = "QA", ["svg"] = "SVG", ["ui"] = "UI", ["cli"] = "CLI",
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
    public void ShouldExposeCliSetupThroughExistingProjectPermissions()
    {
        var catalog = new BuiltInSkillCatalog();
        var skill = catalog.GetById("code-cli-configure").ShouldNotBeNull();
        skill.Kind.ShouldBe(SkillKinds.Playbook);
        skill.AllowedTools.ShouldNotBeNull().ShouldContain("app_security");
        skill.AllowedTools.ShouldContain("process_run");
        skill.AllowedTools.ShouldContain("ask_user");
        skill.Description.ShouldContain("inaccessible");
        skill.Content.ShouldContain("AddDirectoryGrants");
        skill.Content.ShouldContain("existing installation, configuration and caches");
        skill.Content.ShouldContain("visible in project folder settings");
        catalog.GetById("code-run-shell").ShouldNotBeNull().Content.ShouldContain(skill.Id);
    }

    [Fact]
    public void ShouldExposeThePlanPlaybookWithItsTool()
    {
        var plan = new BuiltInSkillCatalog().GetById("chat-plan").ShouldNotBeNull();

        plan.Kind.ShouldBe(SkillKinds.Playbook);
        plan.Aliases.ShouldBe(["todo", "progress"]);
        plan.AllowedTools.ShouldBe(["plan_update"]);
        plan.Content.ShouldContain("Weight each step 1–10");
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
    public void ShouldManageCommandRulesThroughOneSkillThatTheToolSkillsHandOver()
    {
        var catalog = new BuiltInSkillCatalog();
        var rules = catalog.GetById("settings-rules-configure").ShouldNotBeNull();
        rules.Kind.ShouldBe(SkillKinds.Playbook);
        rules.AllowedTools.ShouldBe(["app_read", "app_security", "ask_user"]);
        rules.Content.ShouldContain("SetCommandRule");
        rules.Content.ShouldContain("RemoveCommandRule");
        rules.Content.ShouldContain("resource=CommandRules");
        // A rule is only as wide as asked for; an Allow needs the person's word.
        rules.Content.ShouldContain("Prefer the narrowest pattern");

        foreach (var id in new[] { "settings-tools-configure", "project-tools-configure", "chat-tools-configure" })
        {
            var configure = catalog.GetById(id).ShouldNotBeNull();
            configure.Content.ShouldContain("run_skill` id=settings-rules-configure", customMessage: id);
            configure.Content.ShouldContain("never raise one to Allow because its", customMessage: id);
            configure.AllowedTools.ShouldNotBeNull().ShouldContain("run_skill", id);
        }
        catalog.GetById("settings-tools-reset").ShouldNotBeNull().Content.ShouldContain("RemoveCommandRule");
        catalog.GetById("chat-branch-configure").ShouldNotBeNull().Content.ShouldContain("have no branch level");
    }

    [Fact]
    public void ShouldNeverOfferToDeleteTheChatFromAPlaybookThatRunsInIt()
    {
        var skills = new BuiltInSkillCatalog().List();

        // A run cannot delete the chat it runs in: the messages the turn persists before and after
        // every tool call move that chat's revision past any value a delete could send. No bundled
        // playbook may therefore offer that deletion.
        foreach (var skill in skills.Where(item => item.Kind == SkillKinds.Playbook))
            SkillMarkdown.Body(skill.Content).Contains("delete this chat", StringComparison.Ordinal).ShouldBeFalse(skill.Id);

        // chat-schedule-delete is the one skill that deletes a chat, and only when asked by name.
        var scheduleDelete = skills.Single(item => item.Id == "chat-schedule-delete");
        scheduleDelete.Description.ShouldContain("delete the whole chat if explicitly requested");
        scheduleDelete.AllowedTools.ShouldNotBeNull().ShouldContain("app_chats");

        // chat-summary stays the read-only safe testing example.
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
    public void ShouldTakeTheCurrentTimeFromTheHostInEveryDebugSkill()
    {
        var debug = new BuiltInSkillCatalog().List()
            .Where(skill => skill.Id.StartsWith("debug-", StringComparison.Ordinal)).ToArray();

        debug.Select(skill => skill.Id).Order(StringComparer.Ordinal).ShouldBe(
            ["debug-dump-analyze", "debug-logging-configure", "debug-logs-analyze", "debug-stacktrace-analyze", "debug-trace-analyze"]);
        foreach (var skill in debug)
        {
            // A model's sense of the date is not evidence; relative windows need the real clock.
            skill.Content.ShouldContain("[DateTimeOffset]::Now", customMessage: skill.Id);
            skill.AllowedTools.ShouldNotBeNull().ShouldContain("process_run", skill.Id);
            skill.AllowedTools.ShouldNotContain("write_file", skill.Id);
        }

        // Only the configure skill edits, and only after confirmation; the analyses change nothing.
        foreach (var skill in debug.Where(skill => skill.Id != "debug-logging-configure"))
        {
            skill.Description.ShouldContain("changes nothing", customMessage: skill.Id);
            skill.AllowedTools.ShouldNotBeNull().ShouldNotContain("edit_file", skill.Id);
        }

        var logs = debug.Single(skill => skill.Id == "debug-logs-analyze");
        logs.Content.ShouldContain("Reconstruct the rolling set");
        logs.Content.ShouldContain("Assess whether the logs were enough");
        logs.Content.ShouldContain("debug-logging-configure");
        var configure = debug.Single(skill => skill.Id == "debug-logging-configure");
        configure.AllowedTools.ShouldNotBeNull().ShouldContain("edit_file");
        configure.Content.ShouldContain("never stop, kill or restart a process this run did not start");
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

    [Fact]
    public void ShouldKeepTheMcpImportVerificationContract()
    {
        var skill = new BuiltInSkillCatalog().GetById("mcp-import").ShouldNotBeNull();
        var body = SkillMarkdown.Body(skill.Content);

        // A run's tool catalogue is fixed at start, so discovery never proves a call works now.
        body.ShouldContain("Session availability");
        body.ShouldContain("not in this run's catalogue");
        body.ShouldContain("at most two searches, never a loop");
        // A saved definition is global, so a candidate is proven by a real call before it is saved.
        body.ShouldContain("prove that its command actually runs and that a call produced a real result");
        // The registry is name-only, and GitHub repository search is the capability fallback.
        body.ShouldContain("Search is a substring of server names");
        body.ShouldContain("is the standard fallback");
        // The Windows launcher trap and the prerequisite traps stay documented.
        body.ShouldContain("cmd.exe does not strip single quotes");
        body.ShouldContain("Keep \"not found\" apart from \"cannot");
        body.ShouldContain("environmentVariables");
        // Secrets never travel through arguments, URLs or ordinary environment values.
        body.ShouldContain("Secrets must never be placed");
        // Discovery and functional calls stay separate verdicts.
        body.ShouldContain("Discovery: successful");
        body.ShouldContain("Functional calls: passed");
        // The playbook says nothing about removing a chat: a run cannot delete the one it runs in,
        // because the turn's own messages move the revision past any value a delete could send.
        body.ShouldNotContain("delete this chat");
        skill.AllowedTools.ShouldNotBeNull().ShouldContain("process_run");
        // Creating the chat for a moved verification still needs app_chats, the removal does not.
        skill.AllowedTools.ShouldNotBeNull().ShouldContain("app_chats");
    }
}
