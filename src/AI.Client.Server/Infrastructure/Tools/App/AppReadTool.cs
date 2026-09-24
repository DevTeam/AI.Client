namespace AI.Client.Mcp.App;

using AI.Client.Application.Chats;
using AI.Client.Application.Projects;
using AI.Client.Application.Runs;
using AI.Client.Application.Resources;
using AI.Client.Application.Settings;
using AI.Client.Application.Tools;
using AI.Client.Contracts.Chats;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

/// <summary>What <c>app_read</c> can be pointed at. Every value answers with the same page shape.</summary>
public enum AppResource
{
    /// <summary>All projects, as summaries.</summary>
    Projects,

    /// <summary>One project including its directory grants, MCP server bindings and tool policies.</summary>
    Project,

    /// <summary>The chats of one project, as summaries.</summary>
    Chats,

    /// <summary>One chat's title, endpoint, branches and tool policies, without its messages.</summary>
    Chat,

    /// <summary>The messages of one chat, optionally only those on one branch.</summary>
    Messages,

    /// <summary>Every run the Host is tracking: status, queue, pending approval, last error.</summary>
    Runs,

    /// <summary>Global settings: connections, MCP servers and tool policies. Never any secret.</summary>
    Settings,

    /// <summary>
    /// Messages matching 'query' across every chat, or within whatever 'projectId', 'chatId' and
    /// 'branchId' narrow it to. Returns a snippet around each match, not the whole message.
    /// </summary>
    Search,
    /// <summary>Reusable file and directory references of one project.</summary>
    Resources,
}

[McpServerToolType]
public sealed class AppReadTool(
    IProjectService projects,
    IChatService chats,
    IGlobalSettingsService settings,
    IChatSearchService search,
    IResourceService resources,
    Func<IChatRunDispatcher> runs,
    IAppToolReply reply) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => McpServerTool.Create(
        ReadAsync,
        new McpServerToolCreateOptions
        {
            SerializerOptions = reply.Json,
            Description = "Read this application's own data: projects, chats, messages, runs, resources and global settings. "
                          + "'Project', 'Chat' and 'Messages' need the ids named in their description; the others ignore them. "
                          + "'Search' finds text in messages across every chat at once and needs 'query'; use it instead of reading "
                          + "chats one by one. Results are paged: pass the returned 'nextCursor' back to continue, and expect "
                          + "'truncated' when a page ended on a limit rather than on 'limit' items. API keys are never returned — a "
                          + "connection only reports whether it has one. Call this before any change, because every mutating tool "
                          + "here needs the current revision of what it is changing."
        });

    [McpServerTool(Name = "app_read", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(AppReadResult))]
    private async Task<CallToolResult> ReadAsync(
        AppResource resource,
        Guid? projectId = null,
        Guid? chatId = null,
        Guid? branchId = null,
        string? cursor = null,
        int limit = Paging.DefaultLimit,
        string? query = null,
        bool isRegex = false,
        bool ignoreCase = true,
        string[]? roles = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (resource == AppResource.Search)
                return reply.Reply(await SearchAsync(projectId, chatId, branchId, cursor, limit, query, isRegex, ignoreCase,
                    roles, cancellationToken));
            return reply.Reply(await PageAsync(resource, projectId, chatId, branchId, cursor, limit, cancellationToken));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return reply.Reply(new AppReadResult(resource.ToString(), [], null, false, 0, 0, error.Message), true);
        }
    }

    /// <summary>
    /// Search answers in the same page shape as every other resource, so one tool keeps one result
    /// contract. Its own limits � matches, characters, messages examined � live with the search.
    /// </summary>
    private async Task<AppReadResult> SearchAsync(
        Guid? projectId, Guid? chatId, Guid? branchId, string? cursor, int limit, string? query,
        bool isRegex, bool ignoreCase, string[]? roles, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("'query' is required to search.", nameof(query));
        var found = await search.SearchAsync(new ChatSearchRequest(query, projectId, chatId, branchId,
            isRegex, ignoreCase, roles, null, null, limit, cursor), cancellationToken);
        if (found.Error is { } error) throw new ArgumentException(error, nameof(query));
        var items = found.Matches
            .Select(match => JsonSerializer.SerializeToElement(match, reply.Json))
            .ToArray();
        // 'total' is how many matches this page holds, not how many exist: counting the rest would
        // mean scanning everything, which is the cost the limits exist to avoid.
        return new AppReadResult("Search", items, found.NextCursor, found.Truncated, items.Length, items.Length, null);
    }

    private async Task<AppReadResult> PageAsync(
        AppResource resource, Guid? projectId, Guid? chatId, Guid? branchId, string? cursor, int limit,
        CancellationToken cancellationToken)
    {
        switch (resource)
        {
            case AppResource.Projects:
                return Paging.Page("Projects", await projects.ListAsync(cancellationToken), cursor, limit, reply.Json);
            case AppResource.Project:
            {
                var project = await projects.GetAsync(Required(projectId, nameof(projectId)), cancellationToken)
                    ?? throw new InvalidOperationException("Project not found.");
                return Paging.Page("Project", [project], cursor, limit, reply.Json);
            }
            case AppResource.Chats:
                return Paging.Page("Chats", await chats.ListAsync(Required(projectId, nameof(projectId)), cancellationToken), cursor, limit, reply.Json);
            case AppResource.Chat:
            {
                var chat = await LoadChatAsync(projectId, chatId, cancellationToken);
                // Messages are their own resource: a chat read would otherwise always drag the
                // whole transcript along, and no caller asking for a title wants that.
                return Paging.Page("Chat", [chat with { Messages = [] }], cursor, limit, reply.Json);
            }
            case AppResource.Messages:
            {
                var chat = await LoadChatAsync(projectId, chatId, cancellationToken);
                return Paging.Page("Messages", Branch(chat, branchId), cursor, limit, reply.Json);
            }
            case AppResource.Runs:
            {
                var snapshots = await runs().GetSnapshotAsync(cancellationToken);
                var filtered = snapshots
                    .Where(run => projectId is not { } project || run.ProjectId == project)
                    .Where(run => chatId is not { } chat || run.ChatId == chat)
                    .Where(run => branchId is not { } branch || run.BranchId == branch)
                    .ToArray();
                return Paging.Page("Runs", filtered, cursor, limit, reply.Json);
            }
            case AppResource.Settings:
                return Paging.Page("Settings", [await settings.GetAsync(cancellationToken)], cursor, limit, reply.Json);
            case AppResource.Resources:
                return Paging.Page("Resources", await resources.ListAsync(Required(projectId, nameof(projectId)), cancellationToken),
                    cursor, limit, reply.Json);
            default:
                throw new ArgumentException("Unknown resource.", nameof(resource));
        }
    }

    private async Task<ChatDetails> LoadChatAsync(Guid? projectId, Guid? chatId, CancellationToken cancellationToken) =>
        await chats.GetAsync(Required(projectId, nameof(projectId)), Required(chatId, nameof(chatId)), cancellationToken)
        ?? throw new InvalidOperationException("Chat not found.");

    /// <summary>
    /// The messages a branch actually sees: its head and the chain of parents above it, oldest
    /// first. Sibling branches are left out, which is the same context the model itself is given.
    /// </summary>
    private static IReadOnlyList<ChatMessageView> Branch(ChatDetails chat, Guid? branchId)
    {
        if (branchId is not { } id) return chat.Messages;
        var branch = chat.Branches?.SingleOrDefault(item => item.Id == id)
            ?? throw new InvalidOperationException("Branch not found.");
        var byId = chat.Messages.ToDictionary(message => message.Id);
        var chain = new List<ChatMessageView>();
        var current = branch.HeadMessageId;
        while (current is { } messageId && byId.TryGetValue(messageId, out var message))
        {
            chain.Add(message);
            current = message.ParentId;
        }
        chain.Reverse();
        return chain;
    }

    private static Guid Required(Guid? value, string name) =>
        value ?? throw new ArgumentException($"'{name}' is required for this resource.", name);
}
