namespace AI.Web.Git;

using AI.Contracts.Git;

public interface IGitApi
{
    Task<GitListing> BranchesAsync(string repositoryPath, CancellationToken cancellationToken);
    Task<GitListing> CommitsAsync(string repositoryPath, string? revision, int skip, CancellationToken cancellationToken);
}
