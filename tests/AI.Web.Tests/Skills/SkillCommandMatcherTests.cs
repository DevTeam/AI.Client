namespace AI.Web.Tests.Skills;

using System.Text.Json;
using AI.Contracts.Skills;
using AI.Web.Skills;
using Shouldly;
using Xunit;

public sealed class SkillCommandMatcherTests
{
    private readonly SkillCommandMatcher _matcher = new();

    [Theory]
    [InlineData("c#")]
    [InlineData("cs")]
    [InlineData("CSharp")]
    public void ShouldMatchCSharpExecutionAliases(string query)
    {
        var skill = Skill("code-run-csharp", "Code run csharp", aliases: ["c#", "cs", "csharp"]);
        _matcher.GetQuery("/" + query).ShouldBe(query);
        _matcher.Match([skill], query, []).Single().Skill.Id.ShouldBe(skill.Id);
    }

    [Theory]
    [InlineData("/", "")]
    [InlineData("/ch", "ch")]
    [InlineData("/chat-rename", "chat-rename")]
    [InlineData("hello", null)]
    [InlineData("", null)]
    [InlineData("/ch more", null)]
    [InlineData("/usr/bin", null)]
    [InlineData(" /ch", null)]
    public void ShouldTreatOnlyALeadingSlashWordAsACommand(string text, string? expected) =>
        _matcher.GetQuery(text).ShouldBe(expected);

    [Fact]
    public void ShouldOrderPrefixBeforeWordStartBeforeScatteredBeforeDescription()
    {
        var skills = new[]
        {
            Skill("notes", "Meeting notes", description: "Summarize a code review"),
            Skill("review", "Code review"),
            Skill("commit", "Commit message"),
            Skill("cr", "Create release")
        };

        var matches = _matcher.Match(skills, "co", []);

        matches.Select(item => item.Skill.Id).ShouldBe(["review", "commit", "notes"]);
        matches[0].Highlights.ShouldBe([0, 1]);
        matches[2].Highlights.ShouldBeEmpty();
        _matcher.Match(skills, "rev", []).Select(item => item.Skill.Id).ShouldBe(["review", "notes"]);
        _matcher.Match(skills, "crl", []).Single().Highlights.ShouldBe([0, 1, 9]);
    }

    [Fact]
    public void ShouldShowTheEffectiveEnabledSkillOnceAndRecentFirst()
    {
        var skills = new[]
        {
            Skill("alpha", "Alpha", source: "Built-in"),
            Skill("beta", "Beta", source: "User"),
            Skill("beta", "Beta for project", source: "Project"),
            Skill("gamma", "Gamma", source: "User"),
            Skill("gamma", "Gamma off", source: "Project", enabled: false)
        };

        var matches = _matcher.Match(skills, string.Empty, ["beta"]);

        matches.Select(item => item.Skill.Name).ShouldBe(["Beta for project", "Alpha"]);
    }

    [Fact]
    public void ShouldPutAnExactAliasFirstAndShowIt()
    {
        var skills = new[]
        {
            Skill("chat-compact", "Chat compact"),
            Skill("compare", "Compare files"),
            Skill("chat-context-compact", "Chat context compact", aliases: ["compact"])
        };

        var exact = _matcher.Match(skills, "compact", []);
        exact[0].Skill.Id.ShouldBe("chat-context-compact");
        exact[0].Title.ShouldBe("compact");
        exact[0].Highlights.ShouldBe([0, 1, 2, 3, 4, 5, 6]);

        var prefix = _matcher.Match(skills, "comp", []);
        prefix.Select(item => item.Skill.Id).ShouldBe(["compare", "chat-context-compact", "chat-compact"]);
        prefix[1].Title.ShouldBe("compact");
        prefix[1].Highlights.ShouldBe([0, 1, 2, 3]);
        prefix[0].Alias.ShouldBeNull();
        prefix[0].Title.ShouldBe("Compare files");
    }

    [Fact]
    public void ShouldPreferAMistypedAliasToScatteredNameLetters()
    {
        var skills = new[]
        {
            Skill("chat-compact", "Chat compact"),
            Skill("chat-context-compact", "Chat context compact", aliases: ["cc", "compact"])
        };

        var matches = _matcher.Match(skills, "coma", []);

        matches.Select(item => item.Skill.Id).ShouldBe(["chat-context-compact", "chat-compact"]);
        matches[0].Title.ShouldBe("compact");
        matches[0].Highlights.ShouldBe([0, 1, 2, 4]);
        matches[1].Alias.ShouldBeNull();
    }

    private static SkillDefinition Skill(string id, string name, string description = "Description",
        string source = "User", bool enabled = true, string[]? aliases = null) =>
        new(id, name, description, source, "---", enabled, JsonDocument.Parse("{}").RootElement, Aliases: aliases);
}
