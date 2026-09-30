namespace AI.Server.Hosting.Endpoints;

using Application.Chat;
using Application.Runs;
using Contracts.Chats;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>The summaries that stand in for a chat's earlier history, and compacting on request.</summary>
public sealed class HistoryCheckpointEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/projects/{projectId:guid}/chats/{chatId:guid}/history-checkpoints",
            (Guid projectId, Guid chatId, IHistoryCheckpointService checkpoints, CancellationToken token) =>
                checkpoints.ListAsync(projectId, chatId, token));

        routes.MapDelete("/api/projects/{projectId:guid}/chats/{chatId:guid}/history-checkpoints/{checkpointId:guid}",
            async (Guid projectId, Guid chatId, Guid checkpointId, IHistoryCheckpointService checkpoints, CancellationToken token) =>
                await checkpoints.DeleteAsync(projectId, chatId, checkpointId, token) ? Results.NoContent() : Results.NotFound());

        routes.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/branches/{branchId:guid}/compact",
            async (Guid projectId, Guid chatId, Guid branchId, IChatHistoryCompaction compaction, CancellationToken token) =>
                await compaction.CompactAsync(projectId, chatId, branchId, token) switch
                {
                    null => Results.NotFound(),
                    { Status: HistoryCompactionStatus.Busy } busy => Results.Conflict(busy),
                    { Status: HistoryCompactionStatus.NothingToCompact } nothing => Results.UnprocessableEntity(nothing),
                    { Status: HistoryCompactionStatus.Failed } failed => Results.Json(failed, statusCode: StatusCodes.Status502BadGateway),
                    var done => Results.Ok(done)
                });
    }
}
