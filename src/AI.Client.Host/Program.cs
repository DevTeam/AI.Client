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
using AI.Client.Application.Notifications;
using System.Threading.Channels;
using System.Text.Json;
using System.IO.Compression;
using AI.Client.Infrastructure.Logging;
using AI.Client.Infrastructure.Storage;
using Microsoft.AspNetCore.ResponseCompression;

var builder = WebApplication.CreateBuilder(args);
// One value decides both where logs go and where the container writes its data, so the two can
// never disagree about the storage root.
var storageLocation = new ProjectStorageLocation();
builder.Logging.AddProvider(new JsonLineFileLoggerProvider(storageLocation.RootDirectory));
builder.Services.AddHostedService<ChatRunHostedService>();
// Chat documents can contain many megabytes of tool output. Compress JSON on the wire so the
// browser process does not receive 5-31 MB over loopback for every cache miss. Restricting MIME
// types keeps both SSE endpoints (`text/event-stream`) streaming and unbuffered.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ["application/json", "application/problem+json"];
});
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    options.Level = CompressionLevel.Fastest);
var composition = new Composition(storageLocation.RootDirectory);
builder.Host.UseServiceProviderFactory(composition);

// The frontend runs in a separate process on its own origin (default http://localhost:52174),
// so every API request needs an explicit CORS allowlist. Origins are listed in appsettings.json
// under `Cors:AllowedOrigins`; defaults come from appsettings.Development.json. We deliberately
// avoid `AllowAnyOrigin` — the SPA needs a real origin so credentials and SSE can be added later
// without rewriting the policy.
var corsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders("Content-Disposition"));
});

var app = builder.Build();

app.UseResponseCompression();

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

// CORS runs before endpoint routing so preflight OPTIONS requests are handled before any
// endpoint binding refuses to route them. WebApplication wires the rest of the pipeline itself.
app.UseCors();

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

// Searching is reading, so it stays a GET: the whole request fits in the query string and a result
// is a projection of stored history, never a change to it.
app.MapGet("/api/chats/search", (
    string query, Guid? projectId, Guid? chatId, Guid? branchId, bool? isRegex, bool? ignoreCase,
    string? roles, DateTimeOffset? from, DateTimeOffset? to, int? limit, string? cursor,
    IChatSearchService search, CancellationToken cancellationToken) =>
    search.SearchAsync(new ChatSearchRequest(query, projectId, chatId, branchId,
        isRegex ?? false, ignoreCase ?? true,
        roles?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        from, to, limit ?? ChatSearchLimits.DefaultMatches, cursor), cancellationToken));
app.MapGet("/api/mcp/default/tools", async (AI.Client.Application.Tools.IToolSessionFactory factory, CancellationToken token) =>
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
    timeout.CancelAfter(TimeSpan.FromSeconds(15));
    // Discovery lists what every Host-provided server declares, regardless of whether a project
    // has it switched on: the settings UI is where it gets switched on, and it needs the tools to
    // show first. Grants stay empty, so file system tools remain fail-closed here.
    await using var session = await factory.OpenAsync([],
        new HashSet<Guid> { DefaultMcpServer.Id, AppMcpServer.Id },
        AI.Client.Application.Tools.ToolRunContext.None, timeout.Token);
    return session.Tools.Select(tool => new McpToolInfo(tool.ServerId, tool.OriginalName, tool.ModelDefinition.Description, tool.SchemaHash)).ToArray();
});
app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/tools/decision",
    async (Guid projectId, Guid chatId, Guid branchId, ToolApprovalDecision decision, IChatRunDispatcher dispatcher, CancellationToken token) =>
        await dispatcher.DecideToolAsync(projectId, chatId, branchId, decision, token) ? Results.Ok() : Results.Conflict());

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/prompts/answer",
    async (Guid projectId, Guid chatId, Guid branchId, UserPromptResponse response, IChatRunDispatcher dispatcher, CancellationToken token) =>
        await dispatcher.AnswerPromptAsync(projectId, chatId, branchId, response, token) ? Results.Ok() : Results.Conflict());

app.MapGet("/api/runs/events", async (IChatRunDispatcher dispatcher, IAppDataChangeSignal changes, HttpResponse response, CancellationToken cancellationToken) =>
{
    response.ContentType = "text/event-stream";
    response.Headers.CacheControl = "no-cache";
    // One response, two sources. Frames are funnelled through a channel so that only this loop
    // ever writes to the body: two producers writing to one HTTP response would interleave.
    using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var frames = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    var producers = Task.WhenAll(PublishRunsAsync(stop.Token), PublishDataChangesAsync(stop.Token))
        .ContinueWith(_ => frames.Writer.TryComplete(), TaskScheduler.Default);
    try
    {
        await foreach (var frame in frames.Reader.ReadAllAsync(cancellationToken))
        {
            await response.WriteAsync(frame, cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
    }
    finally
    {
        await stop.CancelAsync();
        try { await producers; } catch (OperationCanceledException) { }
    }

    // The signal says nothing about what moved, so the frame carries only a counter and the client
    // re-reads whatever it is showing. Bursts are held back briefly: a tool that writes ten times
    // in a second should cost the client one reload, not ten.
    async Task PublishDataChangesAsync(CancellationToken token)
    {
        try
        {
            await foreach (var version in changes.SubscribeAsync(token))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), token);
                frames.Writer.TryWrite($"event: data-changed\ndata: {version}\n\n");
            }
        }
        catch (OperationCanceledException) { }
    }

    async Task PublishRunsAsync(CancellationToken token)
    {
        try
        {
            Dictionary<ChatRunKey, ChatRunSnapshot>? previous = null;
            await foreach (var snapshot in dispatcher.SubscribeAsync(token))
            {
                var current = snapshot.ToDictionary(run => new ChatRunKey(run.ChatId, run.BranchId));
                ChatRunSnapshotUpdate update;
                if (previous is null)
                {
                    update = new ChatRunSnapshotUpdate(true, snapshot, [], []);
                }
                else
                {
                    var changed = new List<ChatRunSnapshot>();
                    var appends = new List<ChatRunStreamingAppend>();
                    foreach (var (key, run) in current)
                    {
                        if (!previous.TryGetValue(key, out var old)) changed.Add(run);
                        else if (ReferenceEquals(old, run)) continue;
                        else if (IsStreamingAppend(old, run))
                            appends.Add(new ChatRunStreamingAppend(run.ChatId, run.BranchId, run.Revision,
                                run.StreamingContent[old.StreamingContent.Length..]));
                        else changed.Add(run);
                    }
                    update = new ChatRunSnapshotUpdate(false, changed,
                        previous.Keys.Where(key => !current.ContainsKey(key)).ToArray(), appends);
                }
                previous = current;
                if (!update.IsFull && update.Runs.Count == 0 && update.Removed.Count == 0 && update.StreamingAppends.Count == 0) continue;
                frames.Writer.TryWrite($"event: snapshot\ndata: {JsonSerializer.Serialize(update)}\n\n");
            }
        }
        catch (OperationCanceledException) { }
    }
});

static bool IsStreamingAppend(ChatRunSnapshot old, ChatRunSnapshot current) =>
    current.Revision >= old.Revision
    && current.Status == old.Status
    && current.StreamingContent.Length > old.StreamingContent.Length
    && current.StreamingContent.StartsWith(old.StreamingContent, StringComparison.Ordinal)
    && current.Queue.SequenceEqual(old.Queue)
    && current.HasUnreadResponse == old.HasUnreadResponse
    && current.Error == old.Error
    && current.ChatRevision == old.ChatRevision
    && current.HeadMessageId == old.HeadMessageId
    && Equals(current.PendingApproval, old.PendingApproval)
    && Equals(current.PendingPrompt, old.PendingPrompt)
    && (current.ActiveTools ?? []).SequenceEqual(old.ActiveTools ?? [])
    && current.FailureCode == old.FailureCode
    && current.CanRetry == old.CanRetry
    && current.BranchRevision == old.BranchRevision
    && (current.RecoveryActions ?? []).SequenceEqual(old.RecoveryActions ?? [])
    && current.ActiveMessageId == old.ActiveMessageId
    && Equals(current.WorkspaceChanges, old.WorkspaceChanges);

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/stop",
    (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.StopAsync(projectId, chatId, branchId, cancellationToken, operationId));

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/read",
    (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.MarkReadAsync(projectId, chatId, branchId, cancellationToken, operationId));

app.MapPut("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/{messageId:guid}",
    (Guid projectId, Guid chatId, Guid messageId, Guid branchId, UpdateQueuedMessageRequest request, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.UpdateQueuedAsync(projectId, chatId, branchId, messageId, request, cancellationToken));

app.MapDelete("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/{messageId:guid}",
    (Guid projectId, Guid chatId, Guid messageId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.RemoveQueuedAsync(projectId, chatId, branchId, messageId, cancellationToken, operationId));

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/{messageId:guid}/send-now",
    (Guid projectId, Guid chatId, Guid messageId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.SendQueuedNowAsync(projectId, chatId, branchId, messageId, cancellationToken, operationId));

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/resume",
    (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.ResumeAsync(projectId, chatId, branchId, cancellationToken, operationId));

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/skip",
    (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.SkipFailedAsync(projectId, chatId, branchId, cancellationToken, operationId));

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/rebase",
    (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.RebaseAsync(projectId, chatId, branchId, cancellationToken, operationId));

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/clear",
    (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.ClearAsync(projectId, chatId, branchId, cancellationToken, operationId));

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/discard",
    (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.DiscardAsync(projectId, chatId, branchId, cancellationToken, operationId));

app.MapPost("/api/projects/{projectId:guid}/chats/{chatId:guid}/queue/clear-all",
    (Guid projectId, Guid chatId, Guid branchId, Guid operationId, IChatRunDispatcher dispatcher, CancellationToken cancellationToken) => dispatcher.ClearAllAsync(projectId, chatId, branchId, cancellationToken, operationId));

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

await app.RunAsync();
