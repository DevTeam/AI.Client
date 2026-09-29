namespace AI.Web.Skills;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
        await EnsureAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<SkillDefinition>(cancellationToken)
            ?? throw new InvalidOperationException("Skill save returned no document.");
    }

    public async Task DeleteAsync(SkillDefinition skill, CancellationToken cancellationToken)
    {
        var url = $"api/skills/{Uri.EscapeDataString(skill.Id)}?scope={skill.Source}&revision={skill.Revision}"
            + (skill.ProjectId is { } id ? $"&projectId={id}" : string.Empty);
        using var response = await http.DeleteAsync(url, cancellationToken);
        await EnsureAsync(response, cancellationToken);
    }

    /// <summary>Turns a failed response into the Host's own words rather than its raw JSON.</summary>
    private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new InvalidOperationException("The skill changed elsewhere. Reopen Skills to see the current version.");
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException("The skill no longer exists.");
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body);
            var text = document.RootElement.ValueKind switch
            {
                JsonValueKind.String => document.RootElement.GetString(),
                JsonValueKind.Object when document.RootElement.TryGetProperty("detail", out var detail) => detail.GetString(),
                _ => null
            };
            if (text is { Length: > 0 }) throw new InvalidOperationException(text);
        }
        catch (JsonException)
        {
            // Not a problem document; the status line below is all there is to say.
        }
        throw new InvalidOperationException($"The request failed ({(int)response.StatusCode}).");
    }

    private static string SkillId(string content) => content.ReplaceLineEndings("\n").Split('\n')
        .FirstOrDefault(line => line.StartsWith("id: ", StringComparison.Ordinal))?[4..].Trim()
        ?? throw new ArgumentException("SKILL.md needs id.");
}
