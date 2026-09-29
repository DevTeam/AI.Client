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
        if (id.Length is < 1 or > 64 || id[0] is < 'a' or > 'z'
            || id[^1] == '-' || id.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
            throw new ArgumentException("Skill id must be lowercase letters, digits and hyphens, starting with a letter.");
        var name = Field("name") ?? throw new ArgumentException("SKILL.md needs name.");
        var description = Field("description") ?? throw new ArgumentException("SKILL.md needs description.");
        if (name.Length > 120 || description.Length > 400)
            throw new ArgumentException("Skill name or description is too long.");
        var parameters = Schema(Field("parameters") ?? throw new ArgumentException("SKILL.md needs parameters."));
        var result = Field("result") is { } resultText ? Schema(resultText) : (JsonElement?)null;
        var tools = Field("tools") is { } toolsText
            ? JsonSerializer.Deserialize<string[]>(toolsText) ?? [] : [];
        if (tools.Length > 0)
            throw new ArgumentException("Declarative skills cannot call application tools; pass data in parameters.");
        var enabled = Field("enabled") is not { } enabledText || bool.Parse(enabledText);
        var revision = Field("revision") is { } revisionText
            ? long.Parse(revisionText, System.Globalization.CultureInfo.InvariantCulture) : 0;
        return new SkillDefinition(id, name, description, source, content, enabled, parameters, result,
            tools, projectId, revision);
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

    private static JsonElement Schema(string text)
    {
        var element = JsonSerializer.Deserialize<JsonElement>(text);
        if (element.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("A skill schema must be a JSON object.");
        _ = JsonSchema.Build(element);
        return element;
    }
}
