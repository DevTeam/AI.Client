namespace AI.Application.Skills;

using AI.Contracts.Skills;

/// <summary>How much of each skill a catalog shows, from the most to the least detailed.</summary>
public enum SkillCatalogForm
{
    /// <summary>Every skill as "id: description".</summary>
    Full,

    /// <summary>Every skill as its id and the start of its description.</summary>
    Brief,

    /// <summary>Every skill by its id alone.</summary>
    Ids,

    /// <summary>Skills grouped by the family their id starts with, found with a search for the family.</summary>
    Families
}

/// <summary>One skill as a catalog shows it.</summary>
public sealed record SkillCatalogEntry(string Id, string Description)
{
    public static SkillCatalogEntry Of(SkillDefinition skill) => new(skill.Id, skill.Description);
}

/// <summary>
/// The skill catalog in the most detailed form a budget allows. A model chooses a skill best from
/// the whole list in front of it, as it chooses a word from its own language, which a word search
/// over English metadata cannot do for a request in another language. When the list does not fit,
/// it shortens, and the model searches only within what it was shown.
/// </summary>
public static class SkillCatalogText
{
    public const int BriefDescriptionChars = 60;

    public static readonly IReadOnlyList<SkillCatalogForm> Forms =
        [SkillCatalogForm.Full, SkillCatalogForm.Brief, SkillCatalogForm.Ids, SkillCatalogForm.Families];

    private const string LinePrefix = "- ";

    private const string Rules =
        " Before acting, and when the user changes task, choose the fitting skill and run it with mcp_app__run_skill "
        + "before other tools, then follow its instructions. A skill named by task-specific routing or selected by the "
        + "user takes priority. Continue an active playbook while the user stays on its task. Never guess an id; "
        + "search mcp_app__skill_search by exact id for a skill's argument schema.";

    /// <summary>The standing catalog for the main chat, its rules first.</summary>
    public static string Layer(IReadOnlyList<SkillCatalogEntry> skills, SkillCatalogForm form) =>
        Header(form) + string.Concat(Lines(skills, form).Select(line => "\n" + LinePrefix + line));

    /// <summary>The rules alone, for a budget that has no room for any form of the list.</summary>
    public const string RulesOnly =
        "Skills are tested procedures. When a task starts or the user changes task, find a fitting skill with "
        + "mcp_app__skill_search using English words and run it with mcp_app__run_skill before other tools.";

    /// <summary>The entries of a layer written by <see cref="Layer"/> in its full form.</summary>
    public static IReadOnlyList<SkillCatalogEntry> Parse(string layer) => layer.Split('\n')
        .Where(line => line.StartsWith(LinePrefix, StringComparison.Ordinal))
        .Select(line => line[LinePrefix.Length..].Split(": ", 2))
        .Select(parts => new SkillCatalogEntry(parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : string.Empty))
        .ToArray();

    /// <summary>One line per entry, or per family for <see cref="SkillCatalogForm.Families"/>, without a list marker.</summary>
    public static IReadOnlyList<string> Lines(IReadOnlyList<SkillCatalogEntry> skills, SkillCatalogForm form) => form switch
    {
        SkillCatalogForm.Full => skills.Select(skill => $"{skill.Id}: {Flat(skill.Description)}").ToArray(),
        SkillCatalogForm.Brief => skills.Select(skill => $"{skill.Id}: {Cut(Flat(skill.Description), BriefDescriptionChars)}").ToArray(),
        SkillCatalogForm.Ids => skills.Select(skill => skill.Id).ToArray(),
        _ => skills.GroupBy(skill => Family(skill.Id), StringComparer.Ordinal)
            .Select(group => group.Count() == 1 ? group.Single().Id : $"{group.Key} ({group.Count()} skills)")
            .ToArray()
    };

    /// <summary>The part of an id before its first hyphen: "mermaid" for "mermaid-create-gantt".</summary>
    public static string Family(string id)
    {
        var hyphen = id.IndexOf('-', StringComparison.Ordinal);
        return hyphen > 0 ? id[..hyphen] : id;
    }

    private static string Header(SkillCatalogForm form) => form switch
    {
        SkillCatalogForm.Full => "Skills are tested procedures. This is the complete list of enabled skills, as id: purpose." + Rules,
        SkillCatalogForm.Brief => "Skills are tested procedures. This is the complete list of enabled skills, as id: the start of "
            + "its purpose; mcp_app__skill_search with an id gives the whole description." + Rules,
        SkillCatalogForm.Ids => "Skills are tested procedures. This is the complete list of enabled skill ids; "
            + "mcp_app__skill_search with an id or English task words gives descriptions." + Rules,
        _ => "Skills are tested procedures. Enabled skills are listed by family; mcp_app__skill_search with a family name "
            + "lists its skills, and English task words find them across families." + Rules
    };

    private static string Flat(string text) => text.ReplaceLineEndings(" ").Trim();

    private static string Cut(string text, int length)
    {
        if (text.Length <= length) return text;
        var space = text.LastIndexOf(' ', length);
        return text[..(space > length / 2 ? space : length)].TrimEnd(',', ';', ' ') + "...";
    }
}
