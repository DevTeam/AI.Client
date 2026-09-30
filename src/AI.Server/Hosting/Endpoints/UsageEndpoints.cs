namespace AI.Server.Hosting.Endpoints;

using Application.Usage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>What model requests have used: per chat and its turns, and over a period.</summary>
public sealed class UsageEndpoints : IEndpointModule
{
    /// <summary>The period a report covers when the caller names no start.</summary>
    private static readonly TimeSpan DefaultPeriod = TimeSpan.FromDays(30);

    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/projects/{projectId:guid}/chats/{chatId:guid}/usage",
            (Guid projectId, Guid chatId, ITokenUsageService usage, CancellationToken token) =>
                usage.GetChatAsync(projectId, chatId, token));

        routes.MapGet("/api/usage",
            async (DateTimeOffset? from, DateTimeOffset? to, Guid? projectId, ITokenUsageService usage, CancellationToken token) =>
            {
                var end = to ?? DateTimeOffset.UtcNow;
                var start = from ?? end - DefaultPeriod;
                return start >= end
                    ? Results.Problem("The end of the period must be after its start.", statusCode: StatusCodes.Status400BadRequest)
                    : Results.Ok(await usage.GetReportAsync(start, end, projectId, token));
            });
    }
}
