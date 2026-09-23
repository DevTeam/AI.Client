namespace AI.Client.Server.Hosting.Endpoints;

using Application.Runs;
using Contracts.Runs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

public sealed class RunEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/submit",
            (Guid projectId, Guid chatId, SubmitChatMessageRequest request, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) =>
                dispatcher.SubmitAsync(projectId, chatId, request, cancellationToken));

        routes.MapGet("/api/runs", (IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.GetSnapshotAsync(cancellationToken));

        routes.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/tools/decision",
            async (Guid projectId, Guid chatId, Guid branchId, ToolApprovalDecision decision, IChatRunDispatcher dispatcher, CancellationToken token) =>
                await dispatcher.DecideToolAsync(projectId, chatId, branchId, decision, token) ? Results.Ok() : Results.Conflict());

        routes.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/prompts/answer",
            async (Guid projectId, Guid chatId, Guid branchId, UserPromptResponse response, IChatRunDispatcher dispatcher, CancellationToken token) =>
                await dispatcher.AnswerPromptAsync(projectId, chatId, branchId, response, token) ? Results.Ok() : Results.Conflict());

        // The stream never ends by itself, so it also ends when the server stops: ASP.NET waits for
        // open requests on shutdown, and an open event stream would hold every exit for the full
        // shutdown timeout.
        routes.MapGet("/api/runs/events", async (IRunEventsPublisher events, HttpResponse response, IHostApplicationLifetime lifetime, CancellationToken cancellationToken) =>
        {
            using var stream = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.ApplicationStopping);
            try
            {
                await events.WriteAsync(response, stream.Token);
            }
            catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
            {
            }
        });

        routes.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/stop",
            (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.StopAsync(projectId, chatId, branchId, cancellationToken, operationId));

        routes.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/read",
            (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.MarkReadAsync(projectId, chatId, branchId, cancellationToken, operationId));

        routes.MapPut("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/{messageId:guid}",
            (Guid projectId, Guid chatId, Guid messageId, Guid branchId, UpdateQueuedMessageRequest request, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.UpdateQueuedAsync(projectId, chatId, branchId, messageId, request, cancellationToken));

        routes.MapDelete("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/{messageId:guid}",
            (Guid projectId, Guid chatId, Guid messageId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.RemoveQueuedAsync(projectId, chatId, branchId, messageId, cancellationToken, operationId));

        routes.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/{messageId:guid}/send-now",
            (Guid projectId, Guid chatId, Guid messageId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.SendQueuedNowAsync(projectId, chatId, branchId, messageId, cancellationToken, operationId));

        routes.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/resume",
            (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.ResumeAsync(projectId, chatId, branchId, cancellationToken, operationId));

        routes.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/skip",
            (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.SkipFailedAsync(projectId, chatId, branchId, cancellationToken, operationId));

        routes.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/rebase",
            (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.RebaseAsync(projectId, chatId, branchId, cancellationToken, operationId));

        routes.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/clear",
            (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.ClearAsync(projectId, chatId, branchId, cancellationToken, operationId));

        routes.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/discard",
            (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.DiscardAsync(projectId, chatId, branchId, cancellationToken, operationId));

        routes.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/clear-all",
            (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.ClearAllAsync(projectId, chatId, branchId, cancellationToken, operationId));
    }
}
