using AI.Client.Host;
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
builder.Services.AddHostedService<ChatRunHostedService>();
var composition = new Composition();
builder.Host.UseServiceProviderFactory(composition);

var app = builder.Build();

app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (Exception error) when (!context.Response.HasStarted && error is ArgumentException or AI.Client.Domain.Common.DomainException)
    {
        await Results.Problem(error.Message, statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context);
    }
    catch (InvalidOperationException error) when (!context.Response.HasStarted)
    {
        await Results.Problem(error.Message, statusCode: StatusCodes.Status409Conflict).ExecuteAsync(context);
    }
});


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
    else if (context.Request.Path.StartsWithSegments("/_framework"))
    {
        // Every _framework asset is content-hashed (a changed file gets a new filename), so a
        // successful (200) response is safe to cache forever and needs no help here. A 404 is
        // the dangerous case: it can happen transiently — e.g. a request lands in the narrow
        // window between Kestrel accepting connections and static assets finishing staging on
        // startup — and unlike the hashed filename it's for, that specific 404 is NOT immutable:
        // the same URL starts returning 200 moments later once staging finishes, but the browser
        // has no way to know that from a bare 404 response. Confirmed live: a wasm file that
        // reproducibly 404'd on every normal load (surviving even a manual page reload) started
        // working the instant a request explicitly bypassed the cache — the browser had cached
        // that transient 404 as if it were the asset's permanent state, and kept replaying it
        // indefinitely because the hashed filename never changes to naturally invalidate it, and
        // WASM startup depends on every one of these assemblies loading, so a single poisoned
        // entry breaks the whole app until the user manually clears their cache.
        context.Response.OnStarting(() =>
        {
            if (context.Response.StatusCode == StatusCodes.Status404NotFound)
            {
                context.Response.Headers.CacheControl = "no-store";
            }
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

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/submit",
    (Guid projectId, Guid chatId, SubmitChatMessageRequest request, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) =>
        dispatcher.SubmitAsync(projectId, chatId, request, cancellationToken));

app.MapGet("/api/runs", (IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.GetSnapshotAsync(cancellationToken));
app.MapGet("/api/mcp/default/tools", async (AI.Client.Application.Tools.IToolSessionFactory factory, CancellationToken token) =>
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
    timeout.CancelAfter(TimeSpan.FromSeconds(15));
    await using var session = await factory.OpenAsync([], timeout.Token);
    return session.Tools.Select(tool => new McpToolInfo(tool.ServerId, tool.OriginalName, tool.Definition.Description, tool.SchemaHash)).ToArray();
});
app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/tools/decision",
    async (Guid projectId, Guid chatId, Guid branchId, ToolApprovalDecision decision, IChatRunDispatcher dispatcher, CancellationToken token) =>
        await dispatcher.DecideToolAsync(projectId, chatId, branchId, decision, token) ? Results.Ok() : Results.Conflict());

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

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/stop",
    (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.StopAsync(projectId, chatId, branchId, cancellationToken, operationId));

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/read",
    (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.MarkReadAsync(projectId, chatId, branchId, cancellationToken, operationId));

app.MapPut("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/{messageId:guid}",
    (Guid projectId, Guid chatId, Guid messageId, Guid branchId, UpdateQueuedMessageRequest request, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.UpdateQueuedAsync(projectId, chatId, branchId, messageId, request, cancellationToken));

app.MapDelete("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/{messageId:guid}",
    (Guid projectId, Guid chatId, Guid messageId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.RemoveQueuedAsync(projectId, chatId, branchId, messageId, cancellationToken, operationId));

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/resume",
    (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.ResumeAsync(projectId, chatId, branchId, cancellationToken, operationId));

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/skip",
    (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.SkipFailedAsync(projectId, chatId, branchId, cancellationToken, operationId));

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/rebase",
    (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.RebaseAsync(projectId, chatId, branchId, cancellationToken, operationId));

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/clear",
    (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.ClearAsync(projectId, chatId, branchId, cancellationToken, operationId));

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

app.MapDelete("/api/settings/mcp/{serverId:guid}/tool-policies",
    (Guid serverId, string name, string schemaHash, IGlobalSettingsService service, CancellationToken token) =>
        service.RemoveToolPolicyAsync(serverId, name, schemaHash, token));

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

app.MapPut("/api/projects/{projectId:guid}/chats/{chatId:guid}/tool-policies",
    async (Guid projectId, Guid chatId, ToolPolicySettings policy, IChatService service, CancellationToken token) =>
        await service.SetToolPolicyAsync(projectId, chatId, policy, token) is { } chat ? Results.Ok(chat) : Results.NotFound());

app.MapDelete("/api/projects/{projectId:guid}/chats/{chatId:guid}/tool-policies/{serverId:guid}",
    async (Guid projectId, Guid chatId, Guid serverId, string name, string schemaHash, IChatService service, CancellationToken token) =>
        await service.RemoveToolPolicyAsync(projectId, chatId, serverId, name, schemaHash, token) is { } chat ? Results.Ok(chat) : Results.NotFound());

app.MapPut(
    "/api/projects/{projectId:guid}/chats/{chatId:guid}/title",
    async (Guid projectId, Guid chatId, RenameChatRequest request, IChatService service, CancellationToken cancellationToken) =>
    {
        var chat = await service.RenameAsync(projectId, chatId, request, cancellationToken);
        return chat is null ? Results.Conflict() : Results.Ok(chat);
    });

app.MapPut(
    "/api/projects/{projectId:guid}/chats/{chatId:guid}/pin",
    async (Guid projectId, Guid chatId, PinChatRequest request, IChatService service, CancellationToken cancellationToken) =>
    {
        var chat = await service.PinAsync(projectId, chatId, request, cancellationToken);
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
    async (Guid projectId, Guid chatId, Guid branchId, long revision, IChatService _, IChatRunDispatcher runs, CancellationToken cancellationToken) =>
    {
        var result = await runs.DeleteBranchAsync(projectId, chatId, branchId, revision, cancellationToken);
        return result.IsDeleted ? Results.Ok(result) : result.Revision == 0 ? Results.NotFound() : Results.Conflict(result);
    });

app.MapDelete(
    "/api/projects/{projectId:guid}/chats/{chatId:guid}",
    async (Guid projectId, Guid chatId, long revision, IChatService _, IChatRunDispatcher runs, CancellationToken cancellationToken) =>
    {
        var result = await runs.DeleteChatAsync(projectId, chatId, revision, cancellationToken);
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

app.MapPut("/api/projects/{id:guid}/tool-policies",
    async (Guid id, ToolPolicySettings policy, IProjectService service, CancellationToken token) =>
        await service.SetToolPolicyAsync(id, policy, token) is { } project ? Results.Ok(project) : Results.NotFound());

app.MapDelete("/api/projects/{id:guid}/tool-policies/{serverId:guid}",
    async (Guid id, Guid serverId, string name, string schemaHash, IProjectService service, CancellationToken token) =>
        await service.RemoveToolPolicyAsync(id, serverId, name, schemaHash, token) is { } project ? Results.Ok(project) : Results.NotFound());

app.MapDelete(
    "/api/projects/{id:guid}",
    async (Guid id, long revision, IProjectService _, IChatRunDispatcher runs, CancellationToken cancellationToken) =>
    {
        var result = await runs.DeleteProjectAsync(id, revision, cancellationToken);
        return result.IsDeleted
            ? Results.NoContent()
            : result.Revision == 0 ? Results.NotFound() : Results.Conflict(result);
    });

app.MapFallbackToFile("index.html");

await app.RunAsync();
