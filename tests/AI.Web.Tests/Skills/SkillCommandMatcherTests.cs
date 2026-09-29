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

    private static SkillDefinition Skill(string id, string name, string description = "Description",
        string source = "User", bool enabled = true) =>
        new(id, name, description, source, "---", enabled, JsonDocument.Parse("{}").RootElement);
}
