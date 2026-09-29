namespace AI.Web.Skills;

using System.Net.Http.Json;
using AI.Contracts.Skills;

public sealed class SkillApi(HttpClient http) : ISkillApi
{
    public async Task<IReadOnlyList<SkillDefinition>> ListAsync(CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<IReadOnlyList<SkillDefinition>>("api/skills", cancellationToken) ?? [];
}
