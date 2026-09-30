namespace AI.Server.Hosting.Endpoints;

using Application.Chats;
using Application.Runs;
using Contracts.Chats;
using Contracts.Projects;
using Contracts.Resources;
using Application.Resources;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

public sealed class ChatEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/projects/{projectId:guid}/resources",
            async (Guid projectId, CreateResourceRequest request, IResourceService service, CancellationToken token) =>
                Results.Ok(await service.CreateAsync(projectId, request.Kind, request.Path, token)));
        routes.MapPost("/api/projects/{projectId:guid}/resources/resolve",
            async (Guid projectId, ResolvePathsRequest request, IWorkspacePathResolver resolver, CancellationToken token) =>
                Results.Ok(await resolver.ResolveAsync(projectId, request.Paths ?? [], token)));
        routes.MapGet("/api/projects/{projectId:guid}/resources/search",
            async (Guid projectId, string? query, int? limit, IWorkspaceFileSearch search, CancellationToken token) =>
                Results.Ok(await search.SearchAsync(projectId, query ?? string.Empty, limit ?? 30, token)));
        routes.MapPost("/api/projects/{projectId:guid}/resources/index",
            async (Guid projectId, IWorkspaceFileSearch search, CancellationToken token) =>
            {
                await search.Warm(projectId, token);
                return Results.Accepted();
            });
        routes.MapGet("/api/projects/{projectId:guid}/resources/diffs",
            async (Guid projectId, IResourceService service, CancellationToken token) =>
                Results.Ok(await service.ListDiffSourcesAsync(projectId, token)));
        routes.MapGet("/api/projects/{projectId:guid}/chats/{chatId:guid}/reviews",
            (Guid projectId, Guid chatId, IReviewService service, CancellationToken token) =>
                service.ListAsync(projectId, chatId, token));
        routes.MapGet("/api/projects/{projectId:guid}/chats/{chatId:guid}/reviews/{reviewId:guid}",
            async (Guid projectId, Guid chatId, Guid reviewId, IReviewService service, CancellationToken token) =>
                await service.GetAsync(projectId, chatId, reviewId, token) is { } review
                    ? Results.Ok(review) : Results.NotFound());
        routes.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/reviews",
            async (Guid projectId, Guid chatId, CreateReviewRequest request, IReviewService service, CancellationToken token) =>
                Results.Ok(await service.CreateAsync(projectId, chatId, request, token)));
        routes.MapPut("/api/projects/{projectId:guid}/chats/{chatId:guid}/reviews/{reviewId:guid}",
            async (Guid projectId, Guid chatId, Guid reviewId, UpdateReviewRequest request,
                IReviewService service, CancellationToken token) =>
                await service.UpdateAsync(projectId, chatId, reviewId, request, token) is { } review
                    ? Results.Ok(review) : Results.NotFound());
        routes.MapDelete("/api/projects/{projectId:guid}/chats/{chatId:guid}/reviews/{reviewId:guid}",
            async (Guid projectId, Guid chatId, Guid reviewId, IReviewService service, CancellationToken token) =>
                await service.DeleteAsync(projectId, chatId, reviewId, token)
                    ? Results.NoContent() : Results.NotFound());
        // Searching is reading, so it stays a GET: the whole request fits in the query string and a result
        // is a projection of stored history, never a change to it.
        routes.MapGet("/api/chats/search", (
            string query, Guid? projectId, Guid? chatId, Guid? branchId, bool? isRegex, bool? ignoreCase,
            string? roles, DateTimeOffset? from, DateTimeOffset? to, int? limit, string? cursor, ChatArchiveScope? archiveScope,
            ChatSearchOrder? order, IChatSearchService search, CancellationToken cancellationToken) =>
            search.SearchAsync(new ChatSearchRequest(query, projectId, chatId, branchId,
                isRegex ?? false, ignoreCase ?? true,
                roles?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                from, to, limit ?? ChatSearchLimits.DefaultMatches, cursor, archiveScope ?? ChatArchiveScope.Active,
                order ?? ChatSearchOrder.Stable), cancellationToken));

        routes.MapPost("/api/projects/{projectId:guid}/chats/archive/preview",
            (Guid projectId, ChatArchivePreviewRequest request, IChatArchiveService service, CancellationToken token) =>
                service.PreviewAsync(projectId, request, token));
        routes.MapPost("/api/projects/{projectId:guid}/chats/archive",
            (Guid projectId, ChatArchiveRequest request, IChatArchiveService service, CancellationToken token) =>
                service.ApplyAsync(projectId, request, token));
        routes.MapPost("/api/projects/{projectId:guid}/chats/archive/{operationId:guid}/undo",
            (Guid projectId, Guid operationId, IChatArchiveService service, CancellationToken token) =>
                service.UndoAsync(projectId, operationId, token));

        routes.MapGet(
            "/api/projects/{projectId:guid}/chats",
            (Guid projectId, IChatService service, CancellationToken cancellationToken) => service.ListAsync(projectId, cancellationToken));

        routes.MapGet(
            "/api/projects/{projectId:guid}/chats/{chatId:guid}",
            async (Guid projectId, Guid chatId, IChatService service, CancellationToken cancellationToken) =>
            {
                var chat = await service.GetAsync(projectId, chatId, cancellationToken);
                return chat is null ? Results.NotFound() : Results.Ok(chat);
            });

        routes.MapGet(
            "/api/projects/{projectId:guid}/chats/{chatId:guid}/transcript",
            async (Guid projectId, Guid chatId, IChatService service, CancellationToken cancellationToken) =>
            {
                var chat = await service.GetTranscriptAsync(projectId, chatId, cancellationToken);
                return chat is null ? Results.NotFound() : Results.Ok(chat);
            });

        routes.MapGet(
            "/api/projects/{projectId:guid}/chats/{chatId:guid}/turns/{turnId:guid}/activity",
            async (Guid projectId, Guid chatId, Guid turnId, Guid branchLeafId, IChatService service, CancellationToken cancellationToken) =>
            {
                var activity = await service.GetTurnActivityAsync(projectId, chatId, turnId, branchLeafId, cancellationToken);
                return activity is null ? Results.NotFound() : Results.Ok(activity);
            });

        routes.MapGet(
            "/api/projects/{projectId:guid}/chats/{chatId:guid}/messages/{messageId:guid}/content",
            async (Guid projectId, Guid chatId, Guid messageId, IChatService service, CancellationToken cancellationToken) =>
            {
                var content = await service.GetMessageContentAsync(projectId, chatId, messageId, cancellationToken);
                return content is null ? Results.NotFound() : Results.Ok(content);
            });

        routes.MapPost(
            "/api/projects/{projectId:guid}/chats",
            async (Guid projectId, CreateChatRequest request, IChatService service, CancellationToken cancellationToken) =>
            {
                var chat = await service.CreateAsync(projectId, request, cancellationToken);
                return Results.Created($"/api/projects/{projectId}/chats/{chat.Id}", chat);
            });

        routes.MapPost(
            "/api/projects/{projectId:guid}/chats/{chatId:guid}/messages",
            async (Guid projectId, Guid chatId, AppendChatMessageRequest request, IChatService service, IChatRunDispatcher runs, IChatBranchIds branchIds, CancellationToken cancellationToken) =>
            {
                var chat = await service.AppendMessageAsync(projectId, chatId, request, cancellationToken);
                if (chat is not null) await runs.ReconcileChatAsync(projectId, chatId, branchIds.Collect(chat), cancellationToken);
                return chat is null ? Results.NotFound() : Results.Ok(chat);
            });

        routes.MapPut(
            "/api/projects/{projectId:guid}/chats/{chatId:guid}/endpoint",
            async (Guid projectId, Guid chatId, UpdateChatEndpointRequest request, IChatService service, CancellationToken cancellationToken) =>
            {
                var chat = await service.UpdateEndpointAsync(projectId, chatId, request, cancellationToken);
                return chat is null ? Results.NotFound() : Results.Ok(chat);
            });

        routes.MapPut(
            "/api/projects/{projectId:guid}/chats/{chatId:guid}/approval-mode",
            async (Guid projectId, Guid chatId, UpdateChatApprovalModeRequest request, IChatService service, CancellationToken cancellationToken) =>
            {
                if (!Enum.IsDefined(request.Mode)) return Results.BadRequest();
                var chat = await service.UpdateApprovalModeAsync(projectId, chatId, request, cancellationToken);
                return chat is null ? Results.NotFound() : Results.Ok(chat);
            });

        routes.MapPut("/api/projects/{projectId:guid}/chats/{chatId:guid}/tool-policies",
            async (Guid projectId, Guid chatId, ToolPolicySettings policy, IChatService service, CancellationToken token) =>
                await service.SetToolPolicyAsync(projectId, chatId, policy, token) is { } chat ? Results.Ok(chat) : Results.NotFound());

        routes.MapDelete("/api/projects/{projectId:guid}/chats/{chatId:guid}/tool-policies/{serverId:guid}",
            async (Guid projectId, Guid chatId, Guid serverId, string name, string schemaHash, IChatService service, CancellationToken token) =>
                await service.RemoveToolPolicyAsync(projectId, chatId, serverId, name, schemaHash, token) is { } chat ? Results.Ok(chat) : Results.NotFound());

        routes.MapPut(
            "/api/projects/{projectId:guid}/chats/{chatId:guid}/title",
            async (Guid projectId, Guid chatId, RenameChatRequest request, IChatService service, CancellationToken cancellationToken) =>
            {
                var chat = await service.RenameAsync(projectId, chatId, request, cancellationToken);
                return chat is null ? Results.Conflict() : Results.Ok(chat);
            });

        routes.MapPut(
            "/api/projects/{projectId:guid}/chats/{chatId:guid}/pin",
            async (Guid projectId, Guid chatId, PinChatRequest request, IChatService service, CancellationToken cancellationToken) =>
            {
                var chat = await service.PinAsync(projectId, chatId, request, cancellationToken);
                return chat is null ? Results.Conflict() : Results.Ok(chat);
            });

        routes.MapPut(
            "/api/projects/{projectId:guid}/chats/{chatId:guid}/branches/{branchId:guid}/title",
            async (Guid projectId, Guid chatId, Guid branchId, RenameChatBranchRequest request, IChatService service, CancellationToken cancellationToken) =>
            {
                var chat = await service.RenameBranchAsync(projectId, chatId, branchId, request, cancellationToken);
                return chat is null ? Results.Conflict() : Results.Ok(chat);
            });

        routes.MapDelete(
            "/api/projects/{projectId:guid}/chats/{chatId:guid}/branches/{branchId:guid}",
            async (Guid projectId, Guid chatId, Guid branchId, long revision, IChatService _, IChatRunDispatcher runs, CancellationToken cancellationToken) =>
            {
                var result = await runs.DeleteBranchAsync(projectId, chatId, branchId, revision, cancellationToken);
                return result.IsDeleted ? Results.Ok(result) : result.Revision == 0 ? Results.NotFound() : Results.Conflict(result);
            });

        routes.MapDelete(
            "/api/projects/{projectId:guid}/chats/{chatId:guid}",
            async (Guid projectId, Guid chatId, long revision, IChatService _, IChatRunDispatcher runs, CancellationToken cancellationToken) =>
            {
                var result = await runs.DeleteChatAsync(projectId, chatId, revision, cancellationToken);
                return result.IsDeleted
                    ? Results.NoContent()
                    : result.Revision == 0 ? Results.NotFound() : Results.Conflict(result);
            });
    }
}
