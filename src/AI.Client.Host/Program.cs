using AI.Client.Host;
using AI.Client.Application.Chat;
using AI.Client.Application.Chats;
using AI.Client.Application.Projects;
using AI.Client.Application.Settings;
using AI.Client.Contracts.Chat;
using AI.Client.Contracts.Chats;
using AI.Client.Contracts.Projects;
using AI.Client.Contracts.Settings;
using AI.Client.Contracts.Runs;
using AI.Client.Application.Runs;
using System.Text.Json;
using AI.Client.Infrastructure.Logging;
using AI.Client.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);
var storageLocation = new ProjectStorageLocation();
builder.Logging.AddProvider(new JsonLineFileLoggerProvider(storageLocation.RootDirectory));
var composition = new Composition();
builder.Host.UseServiceProviderFactory(composition);

var app = builder.Build();

app.Use(async (context, next) =>
{
    var isBootstrapModule = context.Request.Path.Equals("/_framework/dotnet.js", StringComparison.OrdinalIgnoreCase);
    var isApplicationShell = context.Request.Path == "/" || context.Request.Path.Equals("/index.html", StringComparison.OrdinalIgnoreCase);
    if (isBootstrapModule || isApplicationShell)
    {
        context.Request.Headers.Remove("If-None-Match");
        context.Request.Headers.Remove("If-Modified-Since");
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });
    }

    await next(context);
});

app.MapStaticAssets();

app.MapGet(
    "/api/health",
    (IHostDescriptor metadata) => Results.Ok(
        new
        {
            metadata.ProductName,
            metadata.Version,
            Status = "ready"
        }));

app.MapGet("/api/runs", (IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.GetSnapshotAsync(cancellationToken));

app.MapGet("/api/runs/events", async (IChatRunDispatcher dispatcher, HttpResponse response, CancellationToken cancellationToken) =>
{
    response.ContentType = "text/event-stream";
    response.Headers.CacheControl = "no-cache";
    await foreach (var snapshot in dispatcher.SubscribeAsync(cancellationToken))
    {
        await response.WriteAsync($"event: snapshot\ndata: {JsonSerializer.Serialize(snapshot)}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
});

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue",
    (Guid projectId, Guid chatId, EnqueueChatMessageRequest request, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.EnqueueAsync(projectId, chatId, request, cancellationToken));

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/stop",
    (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.StopAsync(projectId, chatId, branchId, operationId, cancellationToken));

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/read",
    (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.MarkReadAsync(projectId, chatId, branchId, operationId, cancellationToken));

app.MapPut("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/{messageId:guid}",
    (Guid projectId, Guid chatId, Guid messageId, Guid branchId, UpdateQueuedMessageRequest request, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.UpdateQueuedAsync(projectId, chatId, branchId, messageId, request, cancellationToken));

app.MapDelete("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/{messageId:guid}",
    (Guid projectId, Guid chatId, Guid messageId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.RemoveQueuedAsync(projectId, chatId, branchId, messageId, operationId, cancellationToken));

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/resume",
    (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.ResumeAsync(projectId, chatId, branchId, operationId, cancellationToken));

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/clear",
    (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.ClearAsync(projectId, chatId, branchId, operationId, cancellationToken));

app.MapPost(
    "/api/chat/completions",
    (ChatCompletionRequest request, IChatEndpoint endpoint, CancellationToken cancellationToken) =>
        endpoint.HandleAsync(request, cancellationToken));

app.MapGet(
    "/api/settings",
    (IGlobalSettingsService service, CancellationToken cancellationToken) => service.GetAsync(cancellationToken));

app.MapPut(
    "/api/settings",
    (SaveGlobalSettingsRequest request, IGlobalSettingsService service, CancellationToken cancellationToken) =>
        service.SaveAsync(request, cancellationToken));

app.MapPut(
    "/api/settings/connections/{id:guid}/credential",
    async (Guid id, UpdateSecretRequest request, IGlobalSettingsService service, CancellationToken cancellationToken) =>
        await service.SetConnectionCredentialAsync(id, request.Value, cancellationToken)
            ? Results.NoContent() : Results.NotFound());

app.MapPut(
    "/api/settings/mcp/{id:guid}/credential",
    async (Guid id, UpdateSecretRequest request, IGlobalSettingsService service, CancellationToken cancellationToken) =>
        await service.SetMcpCredentialAsync(id, request.Value, cancellationToken)
            ? Results.NoContent() : Results.NotFound());

app.MapPost(
    "/api/chat/completions/stream",
    (ChatCompletionRequest request, IChatEndpoint endpoint, HttpResponse response, CancellationToken cancellationToken) =>
        endpoint.HandleStreamAsync(request, response, cancellationToken));

app.MapGet(
    "/api/projects/{projectId:guid}/chats",
    (Guid projectId, IChatService service, CancellationToken cancellationToken) => service.ListAsync(projectId, cancellationToken));

app.MapGet(
    "/api/projects/{projectId:guid}/chats/{chatId:guid}",
    async (Guid projectId, Guid chatId, IChatService service, CancellationToken cancellationToken) =>
    {
        var chat = await service.GetAsync(projectId, chatId, cancellationToken);
        return chat is null ? Results.NotFound() : Results.Ok(chat);
    });

app.MapPost(
    "/api/projects/{projectId:guid}/chats",
    async (Guid projectId, CreateChatRequest request, IChatService service, CancellationToken cancellationToken) =>
    {
        var chat = await service.CreateAsync(projectId, request, cancellationToken);
        return Results.Created($"/api/projects/{projectId}/chats/{chat.Id}", chat);
    });

app.MapPost(
    "/api/projects/{projectId:guid}/chats/{chatId:guid}/messages",
    async (Guid projectId, Guid chatId, AppendChatMessageRequest request, IChatService service, IChatRunDispatcher runs, CancellationToken cancellationToken) =>
    {
        var chat = await service.AppendMessageAsync(projectId, chatId, request, cancellationToken);
        if (chat is not null) await runs.ReconcileChatAsync(projectId, chatId, ChatBranchIds.Get(chat), cancellationToken);
        return chat is null ? Results.NotFound() : Results.Ok(chat);
    });

app.MapPut(
    "/api/projects/{projectId:guid}/chats/{chatId:guid}/endpoint",
    async (Guid projectId, Guid chatId, UpdateChatEndpointRequest request, IChatService service, CancellationToken cancellationToken) =>
    {
        var chat = await service.UpdateEndpointAsync(projectId, chatId, request, cancellationToken);
        return chat is null ? Results.NotFound() : Results.Ok(chat);
    });

app.MapPut(
    "/api/projects/{projectId:guid}/chats/{chatId:guid}/title",
    async (Guid projectId, Guid chatId, RenameChatRequest request, IChatService service, CancellationToken cancellationToken) =>
    {
        var chat = await service.RenameAsync(projectId, chatId, request, cancellationToken);
        return chat is null ? Results.Conflict() : Results.Ok(chat);
    });

app.MapPut(
    "/api/projects/{projectId:guid}/chats/{chatId:guid}/branches/{branchId:guid}/title",
    async (Guid projectId, Guid chatId, Guid branchId, RenameChatBranchRequest request, IChatService service, CancellationToken cancellationToken) =>
    {
        var chat = await service.RenameBranchAsync(projectId, chatId, branchId, request, cancellationToken);
        return chat is null ? Results.Conflict() : Results.Ok(chat);
    });

app.MapDelete(
    "/api/projects/{projectId:guid}/chats/{chatId:guid}/branches/{branchId:guid}",
    async (Guid projectId, Guid chatId, Guid branchId, long revision, IChatService service, IChatRunDispatcher runs, CancellationToken cancellationToken) =>
    {
        var result = await service.DeleteBranchAsync(projectId, chatId, branchId, revision, cancellationToken);
        if (result.IsDeleted)
        {
            var chat = await service.GetAsync(projectId, chatId, cancellationToken);
            if (chat is not null) await runs.ReconcileChatAsync(projectId, chatId, ChatBranchIds.Get(chat), cancellationToken);
        }
        return result.IsDeleted ? Results.Ok(result) : result.Revision == 0 ? Results.NotFound() : Results.Conflict(result);
    });

app.MapDelete(
    "/api/projects/{projectId:guid}/chats/{chatId:guid}",
    async (Guid projectId, Guid chatId, long revision, IChatService service, IChatRunDispatcher runs, CancellationToken cancellationToken) =>
    {
        var result = await service.DeleteAsync(projectId, chatId, revision, cancellationToken);
        if (result.IsDeleted) await runs.DeleteChatAsync(projectId, chatId, cancellationToken);
        return result.IsDeleted
            ? Results.NoContent()
            : result.Revision == 0 ? Results.NotFound() : Results.Conflict(result);
    });

app.MapGet(
    "/api/projects",
    (IProjectService service, CancellationToken cancellationToken) =>
        service.ListAsync(cancellationToken));

app.MapGet(
    "/api/projects/{id:guid}",
    async (Guid id, IProjectService service, CancellationToken cancellationToken) =>
    {
        var project = await service.GetAsync(id, cancellationToken);
        return project is null ? Results.NotFound() : Results.Ok(project);
    });

app.MapPost(
    "/api/projects",
    async (CreateProjectRequest request, IProjectService service, CancellationToken cancellationToken) =>
    {
        var project = await service.CreateAsync(request, cancellationToken);
        return Results.Created($"/api/projects/{project.Id}", project);
    });

app.MapPut(
    "/api/projects/{id:guid}",
    async (Guid id, UpdateProjectRequest request, IProjectService service, CancellationToken cancellationToken) =>
    {
        var result = await service.UpdateAsync(id, request, cancellationToken);
        return result.Status switch
        {
            ProjectUpdateStatus.Updated => Results.Ok(result.Project),
            ProjectUpdateStatus.Conflict => Results.Conflict(result),
            _ => Results.NotFound()
        };
    });

app.MapPut(
    "/api/projects/{id:guid}/security",
    async (Guid id, UpdateProjectSecurityRequest request, IProjectService service, CancellationToken cancellationToken) =>
    {
        var result = await service.UpdateSecurityAsync(id, request, cancellationToken);
        return result.Status switch
        {
            ProjectUpdateStatus.Updated => Results.Ok(result.Project),
            ProjectUpdateStatus.Conflict => Results.Conflict(result),
            _ => Results.NotFound()
        };
    });

app.MapPut(
    "/api/projects/{id:guid}/endpoints",
    async (Guid id, UpdateEndpointProfilesRequest request, IProjectService service, CancellationToken cancellationToken) =>
    {
        var result = await service.UpdateEndpointProfilesAsync(id, request, cancellationToken);
        return result.Status switch
        {
            ProjectUpdateStatus.Updated => Results.Ok(result.Project),
            ProjectUpdateStatus.Conflict => Results.Conflict(result),
            _ => Results.NotFound()
        };
    });

app.MapPut(
    "/api/projects/{id:guid}/endpoints/{profileId:guid}/credential",
    async (Guid id, Guid profileId, UpdateEndpointCredentialRequest request, IProjectService service, CancellationToken cancellationToken) =>
        await service.SetEndpointCredentialAsync(id, profileId, request.ApiKey, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound());

app.MapDelete(
    "/api/projects/{id:guid}",
    async (Guid id, long revision, IProjectService service, IChatRunDispatcher runs, CancellationToken cancellationToken) =>
    {
        var result = await service.DeleteAsync(id, revision, cancellationToken);
        if (result.IsDeleted) await runs.DeleteProjectAsync(id, cancellationToken);
        return result.IsDeleted
            ? Results.NoContent()
            : result.Revision == 0 ? Results.NotFound() : Results.Conflict(result);
    });

app.MapFallbackToFile("index.html");

await app.RunAsync();
