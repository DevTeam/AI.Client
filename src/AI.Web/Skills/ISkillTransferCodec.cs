namespace AI.Web.Skills;

/// <summary>
/// Skills as text to copy and paste, and as files to save and load. One skill is its SKILL.md; a
/// set is a JSON list of them to copy, or a .zip with a folder per skill to save, which is the
/// Agent Skills layout other clients read and write. SKILL.md files written for those clients,
/// without this app's id, kind and parameters, are adapted on the way in.
/// </summary>
public interface ISkillTransferCodec
{
    /// <summary>A skill as it goes out: without the revision this Host counts, and on unless it says otherwise.</summary>
    SkillTransferItem ToTransfer(string content, bool enabled);

    /// <summary>One SKILL.md, or a JSON list when there are several.</summary>
    string ExportText(IReadOnlyList<SkillTransferItem> skills);

    /// <summary>A .zip with <c>{id}/SKILL.md</c> for each skill.</summary>
    byte[] ExportArchive(IReadOnlyList<SkillTransferItem> skills);

    /// <summary>A SKILL.md, or JSON with a list of them.</summary>
    SkillTransferParseResult Parse(string text);

    /// <summary>A .zip holding SKILL.md files in any folders.</summary>
    SkillTransferParseResult ParseArchive(byte[] archive);

    /// <summary>The same skill under another id and name, for keeping both.</summary>
    SkillTransferItem WithIdentity(SkillTransferItem skill, string id, string name);
}
