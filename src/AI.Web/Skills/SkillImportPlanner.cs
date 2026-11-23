namespace AI.Web.Skills;

using AI.Web.Settings;

public sealed class SkillImportPlanner : ISkillImportPlanner
{
    public SkillImportPlan Plan(SkillTransferParseResult parsed, IReadOnlyList<SkillTransferItem> current, IReadOnlyCollection<string> builtInIds) =>
        new([.. parsed.Skills.Select(item => PlanSkill(item, current, builtInIds))], parsed.Skipped);

    public string UniqueId(string id, IEnumerable<string> taken)
    {
        var ids = taken.ToHashSet(StringComparer.Ordinal);
        var candidate = id;
        var index = 1;
        while (ids.Contains(candidate)) candidate = $"{id}-{++index}";
        return candidate;
    }

    private static SettingsImportCandidate<SkillTransferItem> PlanSkill(
        ImportedSettingsItem<SkillTransferItem> imported,
        IReadOnlyList<SkillTransferItem> current,
        IReadOnlyCollection<string> builtInIds)
    {
        var skill = imported.Settings;
        // The Host refuses a skill that takes a built-in id, so the only way in is a new id.
        if (builtInIds.Contains(skill.Id))
            return new(imported, skill, null, null, SettingsImportMatch.Conflict, [], [], false, false);

        var existing = current.FirstOrDefault(item => item.Id == skill.Id);
        if (existing is null)
            return new(imported, skill, null, null, SettingsImportMatch.New, [], [], false, false);

        var match = existing.Content == skill.Content && existing.Enabled == skill.Enabled
            ? SettingsImportMatch.Same
            : SettingsImportMatch.Conflict;
        return new(imported, skill, existing, skill, match, [], [], false, false);
    }
}
