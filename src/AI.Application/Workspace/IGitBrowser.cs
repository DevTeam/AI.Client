namespace AI.Application.Workspace;

using AI.Contracts.Git;

public interface IGitBrowser
{
    GitListing Branches(string repositoryPath);
    GitListing Commits(string repositoryPath, string? revision, int skip);
}
