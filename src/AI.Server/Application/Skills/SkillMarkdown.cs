namespace AI.Application.Skills;

using System.Text.Json;
using AI.Contracts.Skills;
using Json.Schema;

public static class SkillMarkdown
{
    public const int MaxLength = 48_000;

    public static SkillDefinition Parse(string content, string source, Guid? projectId = null)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length > MaxLength)
            throw new ArgumentException($"SKILL.md must contain 1 to {MaxLength} characters.");
        var lines = content.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length < 4 || lines[0] != "---") throw new ArgumentException("SKILL.md needs frontmatter.");
        var end = Array.IndexOf(lines, "---", 1);
        if (end < 2 || end == lines.Length - 1) throw new ArgumentException("SKILL.md has incomplete frontmatter.");
        string? Field(string key) => lines.Skip(1).Take(end - 1)
            .FirstOrDefault(line => line.StartsWith(key + ": ", StringComparison.Ordinal))?[(key.Length + 2)..].Trim();
        var id = Field("id") ?? throw new ArgumentException("SKILL.md needs id.");
        if (!IsCommand(id))
            throw new ArgumentException("Skill id must be lowercase letters, digits and hyphens, starting with a letter.");
        var aliases = Field("aliases") is { } aliasesText
            ? JsonSerializer.Deserialize<string[]>(aliasesText) ?? [] : [];
        if (aliases.Length > 8 || aliases.Any(alias => !IsCommand(alias) || alias == id)
            || aliases.Distinct(StringComparer.Ordinal).Count() != aliases.Length)
            throw new ArgumentException("Skill aliases are up to 8 distinct commands spelled like an id and different from it.");
        var icon = Field("icon");
        if (icon is not null && !SkillIcons.IsValid(icon))
            throw new ArgumentException($"Skill icon must be one of: {string.Join(", ", SkillIcons.Names)}; "
                + $"or SVG path data on a 24x24 grid, starting with M, up to {SkillIcons.MaxPathLength} characters.");
        var name = Field("name") ?? throw new ArgumentException("SKILL.md needs name.");
        var description = Field("description") ?? throw new ArgumentException("SKILL.md needs description.");
        if (name.Length > 120 || description.Length > 400)
            throw new ArgumentException("Skill name or description is too long.");
        var parameters = Schema(Field("parameters") ?? throw new ArgumentException("SKILL.md needs parameters."));
        var kind = Field("kind") ?? SkillKinds.Generic;
        if (kind is not (SkillKinds.Generic or SkillKinds.Playbook or SkillKinds.Executor))
            throw new ArgumentException("Skill kind must be generic, playbook or executor.");
        if (kind == SkillKinds.Executor && source != "Built-in")
            throw new ArgumentException("Only bundled skills can be executors.");
        var result = Field("result") is { } resultText ? Schema(resultText) : (JsonElement?)null;
        if (kind == SkillKinds.Playbook && result is not null)
            throw new ArgumentException("A playbook has no result schema; the calling model reports its outcome.");
        var tools = Field("tools") is { } toolsText
            ? JsonSerializer.Deserialize<string[]>(toolsText) ?? [] : [];
        if (tools.Length > 0 && kind != SkillKinds.Playbook)
            throw new ArgumentException("Only playbooks declare tools; a generic skill receives its data in parameters.");
        if (tools.Any(tool => tool.Length is < 1 or > 64
                || tool.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '_'))))
            throw new ArgumentException("Tool names must be lowercase letters, digits and underscores.");
        var enabled = Field("enabled") is not { } enabledText || bool.Parse(enabledText);
        var revision = Field("revision") is { } revisionText
            ? long.Parse(revisionText, System.Globalization.CultureInfo.InvariantCulture) : 0;
        return new SkillDefinition(id, name, description, source, content, enabled, parameters, result,
            tools, projectId, revision, kind, aliases, icon);
    }

    public static string WithMetadata(string content, long revision, bool enabled)
    {
        var lines = content.ReplaceLineEndings("\n").Split('\n').ToList();
        if (lines.Count < 2 || lines[0] != "---" || !lines.Skip(1).Contains("---"))
            throw new ArgumentException("SKILL.md needs frontmatter.");
        lines.RemoveAll(line => line.StartsWith("revision: ", StringComparison.Ordinal)
            || line.StartsWith("enabled: ", StringComparison.Ordinal));
        lines.Insert(1, $"revision: {revision}");
        lines.Insert(2, $"enabled: {enabled.ToString().ToLowerInvariant()}");
        return string.Join('\n', lines);
    }

    /// <summary>The instructions after the frontmatter, which is what a playbook hands to the calling model.</summary>
    public static string Body(string content)
    {
        var lines = content.ReplaceLineEndings("\n").Split('\n');
        var end = Array.IndexOf(lines, "---", 1);
        return string.Join('\n', lines.Skip(end + 1)).Trim();
    }

    private static bool IsCommand(string text) =>
        text.Length is >= 1 and <= 64 && text[0] is >= 'a' and <= 'z' && text[^1] != '-'
        && text.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private static JsonElement Schema(string text)
    {
        var element = JsonSerializer.Deserialize<JsonElement>(text);
        if (element.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("A skill schema must be a JSON object.");
        _ = JsonSchema.Build(element);
        return element;
    }
}
