namespace AI.Application.Skills;

using System.Reflection;
using System.Text.Json;
using AI.Contracts.Skills;

/// <summary>Bundled skills are part of the Host build and cannot be edited through its data directory.</summary>
public sealed class BuiltInSkillCatalog : ISkillCatalog
{
    private readonly IReadOnlyList<SkillDefinition> _skills;

    public BuiltInSkillCatalog()
    {
        var assembly = typeof(BuiltInSkillCatalog).Assembly;
        _skills = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".SKILL.md", StringComparison.Ordinal))
            .Select(name => Read(assembly, name))
            .OrderBy(skill => skill.Name, StringComparer.Ordinal)
            .ToArray();
        if (_skills.GroupBy(skill => skill.Id, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new InvalidOperationException("Two bundled skills have the same ID.");
    }

    public IReadOnlyList<SkillDefinition> List() => _skills;

    public SkillDefinition? GetById(string id) => _skills.FirstOrDefault(skill => skill.Id == id);

    private static SkillDefinition Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Bundled skill '{resource}' is missing.");
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        var lines = content.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length < 4 || lines[0] != "---")
            throw new InvalidOperationException($"Bundled skill '{resource}' has no frontmatter.");
        var end = Array.IndexOf(lines, "---", 1);
        if (end < 2) throw new InvalidOperationException($"Bundled skill '{resource}' has invalid frontmatter.");
        string Field(string key) => lines.Skip(1).Take(end - 1)
            .FirstOrDefault(line => line.StartsWith(key + ": ", StringComparison.Ordinal))?[(key.Length + 2)..].Trim()
            is { Length: > 0 } value ? value : throw new InvalidOperationException($"Bundled skill '{resource}' needs {key}.");
        JsonElement parameters;
        try
        {
            parameters = JsonSerializer.Deserialize<JsonElement>(Field("parameters"));
            if (parameters.ValueKind != JsonValueKind.Object)
                throw new JsonException("The parameters schema must be an object.");
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException($"Bundled skill '{resource}' has invalid parameters.", error);
        }
        return new SkillDefinition(Field("id"), Field("name"), Field("description"), "Built-in", content, true,
            parameters);
    }
}
