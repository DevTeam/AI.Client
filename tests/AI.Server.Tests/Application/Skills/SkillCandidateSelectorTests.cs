namespace AI.Application.Tests.Skills;

using AI.Application.Skills;
using AI.Contracts.Skills;
using Shouldly;
using Xunit;

public sealed class SkillCandidateSelectorTests
{
    private readonly SkillCandidateSelector _selector = new();
    private readonly IReadOnlyList<SkillDefinition> _skills = new BuiltInSkillCatalog().List()
        .Where(skill => skill.Enabled && (skill.Kind != SkillKinds.Executor || skill.Id == "chat-rename"))
        .ToArray();

    [Theory]
    [InlineData("Create a project with a .NET app", "project-create")]
    [InlineData("Run the tests", "code-tests-run")]
    [InlineData("Import an MCP server", "mcp-import")]
    [InlineData("Create a gitgraph diagram", "mermaid-create-gitgraph")]
    [InlineData("Rename this chat", "chat-rename")]
    [InlineData("summary", "chat-summary")]
    [InlineData("diagrams", "mermaid-create")]
    [InlineData("bug report.", "qa-bug-report")]
    [InlineData("Configure gh CLI configuration and cache directory access", "code-cli-configure")]
    [InlineData("process_run cannot access npm installation and cache directories", "code-cli-configure")]
    public void ShouldRetrieveRelevantSkillsWithinTheRoutingLimit(string request, string expectedId)
    {
        var candidates = _selector.FindCandidates(request, _skills, SkillRouteSkill.MaxCandidates);

        candidates.Select(skill => skill.Id).ShouldContain(expectedId);
        candidates.Count.ShouldBeLessThanOrEqualTo(SkillRouteSkill.MaxCandidates);
    }

    [Theory]
    [InlineData("run tests", "code-tests-run")]
    [InlineData("sequence diagram", "mermaid-create-sequence")]
    [InlineData("gantt", "mermaid-create-gantt")]
    public void ShouldRankTheSkillNamedByAShortQueryFirst(string query, string expectedId)
    {
        _selector.FindCandidates(query, _skills, 5)[0].Id.ShouldBe(expectedId);
    }

    [Fact]
    public void ShouldListAFamilyByItsName()
    {
        var family = _skills.Where(skill => skill.Id.StartsWith("mermaid-", StringComparison.Ordinal)).Select(skill => skill.Id);

        var found = _selector.FindCandidates("mermaid", _skills, 50).Select(skill => skill.Id).ToArray();

        // The alias names one skill exactly, and the family follows it.
        found[0].ShouldBe("mermaid-create");
        family.ShouldBeSubsetOf(found);
    }

    [Fact]
    public void ShouldNotMatchAWordInsideAnotherWord()
    {
        var skills = new[] { Skill("thread-dump", "Inspect a thread dump"), Skill("file-read", "Read a file") };

        _selector.FindCandidates("read", skills, 5).Select(skill => skill.Id).ShouldBe(["file-read"]);
    }

    [Fact]
    public void ShouldKeepTheActiveSkillForAShortFollowUp()
    {
        _selector.FindCandidates("Yes", _skills, SkillRouteSkill.MaxCandidates, "code-feature-implement")
            [0].Id.ShouldBe("code-feature-implement");
    }

    [Fact]
    public void ShouldNotSubstituteUnrelatedSkillsForAnUnknownQuery()
    {
        _selector.FindCandidates("zzxqv qqwrt", _skills, SkillRouteSkill.MaxCandidates).ShouldBeEmpty();
    }

    [Fact]
    public void ShouldFindEveryEnabledSkillByItsExactId()
    {
        foreach (var skill in _skills)
            _selector.FindCandidates(skill.Id, _skills, 1).Single().Id.ShouldBe(skill.Id);
    }

    private static SkillDefinition Skill(string id, string description) => new BuiltInSkillCatalog().List()[0] with
    {
        Id = id, Name = id, Description = description, Aliases = null
    };
}
