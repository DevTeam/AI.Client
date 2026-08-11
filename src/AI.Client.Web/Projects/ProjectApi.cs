using AI.Client.Contracts.Projects;
using System.Net;
using System.Net.Http.Json;

namespace AI.Client.Web.Projects;

public sealed class ProjectApi(HttpClient httpClient) : IProjectApi
{
    public async Task<IReadOnlyList<ProjectSummary>> ListAsync(CancellationToken cancellationToken) =>
        await httpClient.GetFromJsonAsync<IReadOnlyList<ProjectSummary>>("api/projects", cancellationToken)
        ?? [];

    public async Task<ProjectDetails?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"api/projects/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ProjectDetails>(cancellationToken);
    }

    public async Task<ProjectDetails> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("api/projects", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ProjectDetails>(cancellationToken)
            ?? throw new InvalidOperationException("Project creation response is empty.");
    }

    public async Task<ProjectUpdateResult> UpdateAsync(
        Guid id,
        UpdateProjectRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync($"api/projects/{id}", request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return ProjectUpdateResult.NotFound();
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return await response.Content.ReadFromJsonAsync<ProjectUpdateResult>(cancellationToken)
                ?? throw new InvalidOperationException("Project conflict response is empty.");
        }

        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<ProjectDetails>(cancellationToken)
            ?? throw new InvalidOperationException("Project update response is empty.");
        return ProjectUpdateResult.Updated(project);
    }

    public async Task<ProjectSecurityUpdateResult> UpdateSecurityAsync(
        Guid id,
        UpdateProjectSecurityRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync($"api/projects/{id}/security", request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return ProjectSecurityUpdateResult.NotFound();
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return await response.Content.ReadFromJsonAsync<ProjectSecurityUpdateResult>(cancellationToken)
                ?? throw new InvalidOperationException("Project security conflict response is empty.");
        }

        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<ProjectDetails>(cancellationToken)
            ?? throw new InvalidOperationException("Project security update response is empty.");
        return ProjectSecurityUpdateResult.Updated(project);
    }

    public async Task<ProjectUpdateResult> UpdateEndpointProfilesAsync(
        Guid id,
        UpdateEndpointProfilesRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync($"api/projects/{id}/endpoints", request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return ProjectUpdateResult.NotFound();
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return await response.Content.ReadFromJsonAsync<ProjectUpdateResult>(cancellationToken)
                ?? throw new InvalidOperationException("Endpoint profile conflict response is empty.");
        }

        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<ProjectDetails>(cancellationToken)
            ?? throw new InvalidOperationException("Endpoint profile update response is empty.");
        return ProjectUpdateResult.Updated(project);
    }

    public async Task<bool> SetEndpointCredentialAsync(
        Guid projectId,
        Guid profileId,
        UpdateEndpointCredentialRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync(
            $"api/projects/{projectId}/endpoints/{profileId}/credential",
            request,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<ProjectDeleteResult> DeleteAsync(Guid id, long revision, CancellationToken cancellationToken)
    {
        using var response = await httpClient.DeleteAsync($"api/projects/{id}?revision={revision}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return ProjectDeleteResult.Deleted(revision);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return ProjectDeleteResult.NotFound();
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return await response.Content.ReadFromJsonAsync<ProjectDeleteResult>(cancellationToken)
                ?? throw new InvalidOperationException("Project conflict response is empty.");
        }

        response.EnsureSuccessStatusCode();
        throw new InvalidOperationException("Unexpected project delete response.");
    }
}
