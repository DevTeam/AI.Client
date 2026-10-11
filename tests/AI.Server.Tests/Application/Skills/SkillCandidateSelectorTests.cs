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
    [InlineData("Создай проект с .NET приложением", "project-create")]
    [InlineData("Run the tests", "code-tests-run")]
    [InlineData("Import an MCP server", "mcp-import")]
    [InlineData("Create a gitgraph diagram", "mermaid-create-gitgraph")]
    [InlineData("Rename this chat", "chat-rename")]
    [InlineData("What did we decide?", "chat-summary")]
    public void ShouldRetrieveRelevantSkillsWithinTheRoutingLimit(string request, string expectedId)
    {
        var candidates = _selector.FindCandidates(request, _skills, SkillRouteSkill.MaxCandidates);

        candidates.Select(skill => skill.Id).ShouldContain(expectedId);
        candidates.Count.ShouldBeLessThanOrEqualTo(SkillRouteSkill.MaxCandidates);
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
        _selector.FindCandidates("zzxqv unrelated nonsense", _skills, SkillRouteSkill.MaxCandidates).ShouldBeEmpty();
    }

    [Fact]
    public void ShouldFindEveryEnabledSkillByItsExactId()
    {
        foreach (var skill in _skills)
            _selector.FindCandidates(skill.Id, _skills, 1).Single().Id.ShouldBe(skill.Id);
    }
}
