namespace AI.Server.Hosting.Endpoints;

using Application.Chats;
using Application.Projects;
using Application.Runs;
using Contracts.Chats;
using Contracts.Projects;
using Contracts.Schedules;
using Contracts.Resources;
using Application.Resources;
using Application.Workspace;
using Contracts.Workspace;
using Contracts.Runs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

public sealed class ChatEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/projects/{projectId:guid}/assets", async (Guid projectId, HttpRequest request,
            IResourceAssetService assets, CancellationToken token) =>
        {
            const int maximumBytes = 15 * 1024 * 1024;
            if (request.ContentLength is > maximumBytes) return Results.Problem("File exceeds 15 MB.", statusCode: 413);
            using var buffer = new MemoryStream();
            var chunk = new byte[64 * 1024];
            while (true)
            {
                var read = await request.Body.ReadAsync(chunk, token);
                if (read == 0) break;
                if (buffer.Length + read > maximumBytes) return Results.Problem("File exceeds 15 MB.", statusCode: 413);
                buffer.Write(chunk, 0, read);
            }
            var source = request.Query["source"] == "clipboard" ? ChatResourceSource.Clipboard : ChatResourceSource.Upload;
            try
            {
                return Results.Ok(await assets.StoreAsync(projectId, buffer.ToArray(),
                    request.Query["name"].ToString(), source, request.Query["name"].ToString(), token));
            }
            catch (InvalidDataException error) { return Results.Problem(error.Message, statusCode: 422); }
            catch (FileNotFoundException) { return Results.NotFound(); }
        });
        routes.MapGet("/api/projects/{projectId:guid}/assets/{assetId}/ticket", async (Guid projectId,
            string assetId, IResourceAssetService assets, CancellationToken token) =>
            await assets.CreateTicketAsync(projectId, assetId, token) is { } ticket
                ? Results.Ok(ticket) : Results.NotFound());
        routes.MapGet("/api/projects/{projectId:guid}/assets/{assetId}/text", async (Guid projectId,
            string assetId, IResourceAssetService assets, CancellationToken token) =>
            await assets.ReadTextAsync(projectId, assetId, token) is { } text
                ? Results.Ok(text) : Results.NoContent());
        routes.MapGet("/asset-content/{ticket}", async (string ticket, IResourceAssetService assets,
            HttpContext context, CancellationToken token) =>
        {
            var asset = await assets.ReadTicketAsync(ticket, token);
            if (asset is null) return Results.NotFound();
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var image = asset.MediaType is "image/png" or "image/jpeg" or "image/webp" or "image/gif";
            if (!image) context.Response.Headers.ContentDisposition = "attachment";
            return Results.File(asset.Data, image ? asset.MediaType : "application/octet-stream");
        });
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
        // Drafts a comment for the open comment box; neither the review nor the chat changes.
        routes.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/reviews/comment-suggestion",
            async (Guid projectId, Guid chatId, ReviewCommentSuggestionRequest request, IReviewCommentSuggestions suggestions,
                    CancellationToken token) =>
                await suggestions.SuggestAsync(projectId, chatId, request, token) is { } suggestion
                    ? Results.Ok(suggestion) : Results.NoContent());
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
        routes.MapGet("/api/projects/{projectId:guid}/chats/{chatId:guid}/workspace-undo/status/{undoId:guid}",
            async (Guid projectId, Guid chatId, Guid undoId, IWorkspaceUndoService undo, CancellationToken token) =>
                await undo.StatusAsync(projectId, chatId, undoId, token) is { } status
                    ? Results.Ok(status) : Results.NotFound());
        routes.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/workspace-undo/{messageId:guid}",
            async (Guid projectId, Guid chatId, Guid messageId, WorkspaceUndoRequest request,
                IChatService chats, IWorkspaceUndoGuard guard, IWorkspaceUndoService undo, CancellationToken token) =>
            {
                if (await guard.IsProjectWritingAsync(projectId, token))
                    return Results.Conflict("Wait for active project runs before undoing files.");
                var chat = await chats.GetAsync(projectId, chatId, token);
                var id = chat?.Messages.FirstOrDefault(message => message.Id == messageId)?.WorkspaceChanges?.UndoId;
                return id is { } undoId && await undo.UndoAsync(projectId, chatId, undoId, request.Path, token) is { } status
                    ? Results.Ok(status) : Results.NotFound();
            });
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

        // The sidebar's Recents: the chats with the latest activity across every project, as the
        // chat list of each project shows them (no archived chat, no chat without a message yet).
        // A scheduled chat is left out: the sidebar's Scheduled section lists it instead, and if it
        // stayed here it would also spend one of the few places on a chat nothing has happened in.
        routes.MapGet("/api/chats/recent", async (int? limit, IProjectService projects, IChatService service,
            CancellationToken cancellationToken) =>
        {
            var recent = new List<ChatSummary>();
            foreach (var project in await projects.ListAsync(cancellationToken))
                recent.AddRange((await service.ListAsync(project.Id, cancellationToken))
                    .Where(chat => chat.ArchivedAt is null && !chat.IsEmpty && chat.Kind != ChatSchedule.Kind));
            return recent.OrderByDescending(chat => chat.LastActivityAt)
                .Take(Math.Clamp(limit ?? 10, 1, 50)).ToArray();
        });

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
                // Who sent a message and how it reached the branch are the run dispatcher's to say;
                // a client never names a branch as the author of what it writes.
                var chat = await service.AppendMessageAsync(projectId, chatId,
                    request with { Sender = null, Delivery = MessageDelivery.Turn }, cancellationToken);
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
