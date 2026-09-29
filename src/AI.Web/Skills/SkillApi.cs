namespace AI.Web.Skills;

using System.Net.Http.Json;
using AI.Contracts.Skills;

public sealed class SkillApi(HttpClient http) : ISkillApi
{
    public async Task<IReadOnlyList<SkillDefinition>> ListAsync(Guid? projectId, CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<IReadOnlyList<SkillDefinition>>(
            projectId is { } id ? $"api/skills?projectId={id}" : "api/skills", cancellationToken) ?? [];

    public async Task<IReadOnlyList<SkillRunRecord>> ListRunsAsync(CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<IReadOnlyList<SkillRunRecord>>("api/skills/runs", cancellationToken) ?? [];

    public async Task<SkillDefinition> SaveAsync(SkillWriteRequest request, CancellationToken cancellationToken)
    {
        var id = SkillId(request.Content);
        var method = request.Revision == 0 ? HttpMethod.Post : HttpMethod.Put;
        using var message = new HttpRequestMessage(method,
            request.Revision == 0 ? "api/skills" : $"api/skills/{Uri.EscapeDataString(id)}")
        { Content = JsonContent.Create(request) };
        using var response = await http.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await response.Content.ReadAsStringAsync(cancellationToken));
        return await response.Content.ReadFromJsonAsync<SkillDefinition>(cancellationToken)
            ?? throw new InvalidOperationException("Skill save returned no document.");
    }

    public async Task DeleteAsync(SkillDefinition skill, CancellationToken cancellationToken)
    {
        var url = $"api/skills/{Uri.EscapeDataString(skill.Id)}?scope={skill.Source}&revision={skill.Revision}"
            + (skill.ProjectId is { } id ? $"&projectId={id}" : string.Empty);
        using var response = await http.DeleteAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static string SkillId(string content) => content.ReplaceLineEndings("\n").Split('\n')
        .FirstOrDefault(line => line.StartsWith("id: ", StringComparison.Ordinal))?[4..].Trim()
        ?? throw new ArgumentException("SKILL.md needs id.");
}
