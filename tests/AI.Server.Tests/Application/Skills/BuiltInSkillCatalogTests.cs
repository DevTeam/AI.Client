namespace AI.Application.Tests.Skills;

using AI.Application.Skills;
using AI.Contracts.Skills;
using Shouldly;
using Xunit;

public class BuiltInSkillCatalogTests
{
    private static readonly string[] Domains = ["chat", "project", "memory", "skill", "instructions", "code", "git"];

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
    public void ShouldFollowTheSkillNamingSystem()
    {
        foreach (var skill in new BuiltInSkillCatalog().List())
        {
            // <domain>-<action>[-<object>], and the name is the id in words.
            Domains.ShouldContain(skill.Id.Split('-')[0], skill.Id);
            skill.Id.Split('-').Length.ShouldBeGreaterThan(1, skill.Id);
            var words = skill.Id.Replace('-', ' ');
            skill.Name.ShouldBe(char.ToUpperInvariant(words[0]) + words[1..], skill.Id);
            char.IsUpper(skill.Description[0]).ShouldBeTrue(skill.Id);
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
        string[] executors = ["chat-rename", "chat-reply-suggest", "skill-route"];

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
