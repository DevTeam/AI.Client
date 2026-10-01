namespace AI.Server.Hosting.Endpoints;

using AI.Application.Workspace;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>Like the directory picker, Git browsing is a local, read-only host capability.</summary>
public sealed class GitEndpoints(ServerOptions options) : IEndpointModule
{
    public void Map(IEndpointRouteBuilder routes)
    {
        if (!options.BrowseEnabled) return;
        routes.MapGet("/api/git/branches", (string repositoryPath, IGitBrowser browser) =>
            Read(() => browser.Branches(repositoryPath)));
        routes.MapGet("/api/git/commits", (string repositoryPath, string? revision, int? skip, IGitBrowser browser) =>
            Read(() => browser.Commits(repositoryPath, revision, skip ?? 0)));
    }

    private static IResult Read(Func<AI.Contracts.Git.GitListing> read)
    {
        try { return Results.Ok(read()); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { return Results.BadRequest(new { error = error.Message }); }
    }
}
