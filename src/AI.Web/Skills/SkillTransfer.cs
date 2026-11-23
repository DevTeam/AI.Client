namespace AI.Web.Skills;

using AI.Web.Settings;

/// <summary>
/// One skill as it travels: its SKILL.md without the Host's own metadata, and whether it is on.
/// <paramref name="Id"/>, <paramref name="Name"/> and <paramref name="Description"/> are read from
/// the frontmatter for the preview; <paramref name="Content"/> stays the one source of truth.
/// </summary>
public sealed record SkillTransferItem(string Id, string Name, string Description, string Content, bool Enabled);

/// <param name="Skills">What could be read; the notes say what was adapted or left behind.</param>
/// <param name="Skipped">Documents that are not imported at all, and why.</param>
/// <param name="Error">Why nothing could be read; the lists are empty when it is set.</param>
public sealed record SkillTransferParseResult(
    IReadOnlyList<ImportedSettingsItem<SkillTransferItem>> Skills,
    IReadOnlyList<SkippedImportItem> Skipped,
    string? Error)
{
    public static SkillTransferParseResult Failed(string error) => new([], [], error);
}

/// <summary>A .zip of skill folders as it was dropped or loaded.</summary>
public sealed record SkillArchive(string FileName, byte[] Content);

public sealed record SkillImportPlan(
    IReadOnlyList<SettingsImportCandidate<SkillTransferItem>> Skills,
    IReadOnlyList<SkippedImportItem> Skipped);
