namespace AI.Web.Tests.Skills;

using AI.Web.Settings;
using AI.Web.Skills;
using Shouldly;
using Xunit;

public class SkillImportPlannerTests
{
    private const string Skill = "---\nid: release-notes\nname: Release notes\ndescription: Write release notes.\nkind: playbook\nparameters: {}\n---\nBody";

    private readonly SkillTransferCodec _codec = new();
    private readonly SkillImportPlanner _planner = new();

    private SettingsImportCandidate<SkillTransferItem> Plan(string text, IReadOnlyList<SkillTransferItem>? current = null, IReadOnlyCollection<string>? builtIns = null) =>
        _planner.Plan(_codec.Parse(text), current ?? [], builtIns ?? []).Skills.Single();

    [Fact]
    public void ShouldAddANewSkill()
    {
        var candidate = Plan(Skill);

        candidate.Match.ShouldBe(SettingsImportMatch.New);
        candidate.DefaultAction.ShouldBe(SettingsImportAction.Add);
    }

    [Fact]
    public void ShouldLeaveAnIdenticalSkillAlone()
    {
        var candidate = Plan(Skill, [_codec.ToTransfer("---\nrevision: 2\nenabled: true\n" + Skill[4..], true)]);

        candidate.Match.ShouldBe(SettingsImportMatch.Same);
        candidate.DefaultAction.ShouldBe(SettingsImportAction.Skip);
    }

    [Fact]
    public void ShouldOfferToReplaceASkillWithTheSameId()
    {
        var candidate = Plan(Skill, [_codec.ToTransfer(Skill.Replace("Body", "Old body", StringComparison.Ordinal), true)]);

        candidate.Match.ShouldBe(SettingsImportMatch.Conflict);
        candidate.CanReplace.ShouldBeTrue();
        candidate.DefaultAction.ShouldBe(SettingsImportAction.Replace);
    }

    [Fact]
    public void ShouldOnlyKeepBothWhenABuiltInSkillHoldsTheId()
    {
        var candidate = Plan(Skill, builtIns: ["release-notes"]);

        candidate.Match.ShouldBe(SettingsImportMatch.Conflict);
        candidate.CanReplace.ShouldBeFalse();
        candidate.DefaultAction.ShouldBe(SettingsImportAction.KeepBoth);
    }

    [Fact]
    public void ShouldNumberATakenId() =>
        _planner.UniqueId("release-notes", ["release-notes", "release-notes-2"]).ShouldBe("release-notes-3");
}
