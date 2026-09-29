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

    public Task<IReadOnlyList<SkillDefinition>> ListAsync(Guid? projectId, CancellationToken cancellationToken) =>
        Task.FromResult(List());

    public Task<SkillDefinition?> GetByIdAsync(string id, Guid? projectId, CancellationToken cancellationToken) =>
        Task.FromResult(GetById(id));

    public Task<SkillWriteResult> SaveAsync(SkillWriteRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new SkillWriteResult("Rejected", Error: "Bundled skills are read-only."));

    public Task<SkillWriteResult> DeleteAsync(string id, string scope, Guid? projectId, long revision,
        CancellationToken cancellationToken) =>
        Task.FromResult(new SkillWriteResult("Rejected", Error: "Bundled skills are read-only."));

    public Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken) => Task.CompletedTask;

    private static SkillDefinition Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Bundled skill '{resource}' is missing.");
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        try { return SkillMarkdown.Parse(content, "Built-in"); }
        catch (Exception error) when (error is ArgumentException or JsonException or FormatException)
        { throw new InvalidOperationException($"Bundled skill '{resource}' is invalid.", error); }
    }
}
