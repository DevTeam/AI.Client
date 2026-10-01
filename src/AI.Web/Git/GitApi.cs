namespace AI.Web.Git;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.Contracts.Git;

public sealed class GitApi(HttpClient httpClient) : IGitApi
{
    public Task<GitListing> BranchesAsync(string repositoryPath, CancellationToken cancellationToken) =>
        GetAsync($"api/git/branches?repositoryPath={Uri.EscapeDataString(repositoryPath)}", cancellationToken);

    public Task<GitListing> CommitsAsync(string repositoryPath, string? revision, int skip, CancellationToken cancellationToken) =>
        GetAsync($"api/git/commits?repositoryPath={Uri.EscapeDataString(repositoryPath)}&revision={Uri.EscapeDataString(revision ?? string.Empty)}&skip={skip}", cancellationToken);

    private async Task<GitListing> GetAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new HttpRequestException("Git browsing is switched off on the host.");
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var error = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            throw new HttpRequestException(error.GetProperty("error").GetString());
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GitListing>(cancellationToken)
            ?? throw new HttpRequestException("The host returned no Git listing.");
    }
}
