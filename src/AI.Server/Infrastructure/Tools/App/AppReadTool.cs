namespace AI.Mcp.App;

using AI.Application.Chats;
using AI.Application.Instructions;
using AI.Application.Memory;
using AI.Application.Projects;
using AI.Application.Runs;
using AI.Application.Resources;
using AI.Application.Settings;
using AI.Application.Skills;
using AI.Application.Tools;
using AI.Contracts.Chats;
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
    /// <summary>Named reviews created in one chat, across all its branches.</summary>
    Reviews,
    /// <summary>One mutable review, including a bounded number of comments.</summary>
    Review,
    /// <summary>
    /// Long-term memory of the person and of the project: every entry, one entry by resourceId, or
    /// the enabled entries matching 'query'.
    /// </summary>
    Memory,
    /// <summary>
    /// The project's instructions document with its revision, and the size and sources of each standing
    /// system prompt layer.
    /// </summary>
    Instructions,
    /// <summary>
    /// Built-in, User and Project skills of the project with source, kind, enabled state and revision.
    /// 'query' narrows them to an id or text and then also returns each full SKILL.md.
    /// </summary>
    Skills,
}

[McpServerToolType]
public sealed class AppReadTool(
    IProjectService projects,
    IChatService chats,
    IGlobalSettingsService settings,
    IChatSearchService search,
    IResourceService resources,
    IReviewService reviews,
    IMemoryService memory,
    IProjectInstructionsService projectInstructions,
    IStandingInstructions standing,
    ISkillCatalog skills,
    Func<IChatRunDispatcher> runs,
    IAppToolReply reply) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => new Session(this, run, reply).Create();

    private sealed class Session(AppReadTool tool, ToolRunContext run, IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(ReadAsync,
            new McpServerToolCreateOptions
            {
                SerializerOptions = reply.Json,
                Description = "Read this application's own data: projects, chats, messages, runs, resources and global settings. "
                              + "'Projects' and 'Settings' need no ids. 'Project', 'Chats', 'Resources', 'Chat', 'Messages', "
                              + "'Reviews' and 'Review' use projectId; the last four also use chatId, and 'Review' needs resourceId. "
                              + "Omitted chatId for 'Chat', 'Messages' and 'Reviews' means the current chat. "
                              + "'Memory' lists the user's and the project's long-term memory; pass resourceId for one entry or "
                              + "query to search. 'Instructions' returns the project instructions and their revision. 'Skills' lists "
                              + "built-in, User and Project skills; pass a skill id or text as query to get their full SKILL.md "
                              + "and revision before editing one with app_skills. "
                              + "For these project-scoped resources, omitted projectId means the current project. "
                              + "'Runs' accepts optional projectId, chatId and branchId filters. 'Search' accepts the same optional "
                              + "filters and requires query; use it instead of reading chats one by one. Results are paged: pass "
                              + "the returned 'nextCursor' back to continue, and expect 'truncated' when a page ended on a limit "
                              + "rather than on 'limit' items. API keys are never returned — a connection only reports whether "
                              + "it has one. Call this before any change, because every mutating tool here needs the current "
                              + "revision of what it is changing."
            });

        [McpServerTool(Name = "app_read", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
            UseStructuredContent = true, OutputSchemaType = typeof(AppReadResult))]
        private Task<CallToolResult> ReadAsync(
            AppResource resource,
            Guid? projectId = null,
            Guid? chatId = null,
            Guid? branchId = null,
            Guid? resourceId = null,
            string? cursor = null,
            int limit = Paging.DefaultLimit,
            string? query = null,
            bool isRegex = false,
            bool ignoreCase = true,
            string[]? roles = null,
            CancellationToken cancellationToken = default) =>
            tool.ReadAsync(run, resource, projectId, chatId, branchId, resourceId, cursor, limit, query, isRegex,
                ignoreCase, roles, cancellationToken);
    }

    private async Task<CallToolResult> ReadAsync(
        ToolRunContext run,
        AppResource resource,
        Guid? projectId = null,
        Guid? chatId = null,
        Guid? branchId = null,
        Guid? resourceId = null,
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
            if (resource is AppResource.Project or AppResource.Chats or AppResource.Chat or AppResource.Messages
                or AppResource.Resources or AppResource.Reviews or AppResource.Review or AppResource.Memory
                or AppResource.Instructions or AppResource.Skills)
                projectId ??= run.ProjectId == Guid.Empty ? null : run.ProjectId;
            // Chat-scoped reads of the project the run is in default to the run's own chat.
            if (resource is AppResource.Chat or AppResource.Messages or AppResource.Reviews
                && projectId == run.ProjectId && run.ChatId != Guid.Empty)
                chatId ??= run.ChatId;
            if (resource == AppResource.Search)
                return reply.Reply(await SearchAsync(projectId, chatId, branchId, cursor, limit, query, isRegex, ignoreCase,
                    roles, cancellationToken));
            return reply.Reply(await PageAsync(resource, projectId, chatId, branchId, resourceId, cursor, limit, query,
                cancellationToken));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return reply.Reply(new AppReadResult(resource.ToString(), [], null, false, 0, 0, error.Message), true);
        }
    }

    /// <summary>
    /// Search answers in the same page shape as every other resource, so one tool keeps one result
    /// contract. Its own limits — matches, characters, messages examined — live with the search.
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
        AppResource resource, Guid? projectId, Guid? chatId, Guid? branchId, Guid? resourceId, string? cursor, int limit,
        string? query, CancellationToken cancellationToken)
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
            case AppResource.Reviews:
            {
                var items = await reviews.ListAsync(Required(projectId, nameof(projectId)),
                    Required(chatId, nameof(chatId)), cancellationToken);
                var summaries = items.Select(item => new { item.Id, item.Name, item.SourceMessageId,
                    item.SourceCreatedAt, item.Files, CommentCount = item.Comments.Count, item.UpdatedAt }).ToArray();
                return Paging.Page("Reviews", summaries, cursor, limit, reply.Json);
            }
            case AppResource.Review:
            {
                var item = await reviews.GetAsync(Required(projectId, nameof(projectId)),
                    Required(chatId, nameof(chatId)), Required(resourceId, nameof(resourceId)), cancellationToken)
                    ?? throw new InvalidOperationException("Review not found.");
                var bounded = new { item.Id, item.Name, item.SourceMessageId, item.Files,
                    Comments = item.Comments.Take(40).Select(comment => comment with
                    { Body = comment.Body[..Math.Min(comment.Body.Length, 1000)] }).ToArray(),
                    RemainingComments = Math.Max(0, item.Comments.Count - 40), item.Revision };
                return Paging.Page("Review", [bounded], cursor, limit, reply.Json);
            }
            case AppResource.Memory:
            {
                if (resourceId is { } entryId)
                    return Paging.Page("Memory", [await memory.GetAsync(entryId, projectId, cancellationToken)
                        ?? throw new InvalidOperationException("Memory entry not found.")], cursor, limit, reply.Json);
                var entries = string.IsNullOrWhiteSpace(query)
                    ? await memory.ListAsync(projectId, cancellationToken)
                    : await memory.SearchAsync(query, projectId, cancellationToken);
                return Paging.Page("Memory", entries, cursor, limit, reply.Json);
            }
            case AppResource.Instructions:
            {
                var id = Required(projectId, nameof(projectId));
                var stored = await projectInstructions.GetAsync(id, cancellationToken)
                    ?? throw new InvalidOperationException("Project not found.");
                // The layer text itself is already in the model's own system prompt; repeating it
                // here would only spend the context twice.
                var preview = await standing.BuildAsync(id, true, cancellationToken);
                return Paging.Page("Instructions", [new
                {
                    Instructions = stored,
                    Layers = preview.Layers.Select(layer => new
                        { layer.Key, layer.Title, layer.Sources, layer.Tokens, layer.BudgetTokens, layer.Truncated }).ToArray()
                }], cursor, limit, reply.Json);
            }
            case AppResource.Skills:
            {
                var all = await skills.ListAsync(projectId, cancellationToken);
                var matched = string.IsNullOrWhiteSpace(query)
                    ? all
                    : all.Where(skill => skill.Id == query || skill.Aliases?.Contains(query) == true
                        || skill.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || skill.Description.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
                var exact = matched.Where(skill => skill.Id == query || skill.Aliases?.Contains(query) == true).ToArray();
                // Without a query this is a catalog; the documents themselves are only worth their
                // size once the caller has said which skill it is about to change.
                var withContent = !string.IsNullOrWhiteSpace(query);
                return Paging.Page("Skills", (exact.Length > 0 ? exact : matched).Select(skill => new
                {
                    skill.Id, skill.Name, skill.Description, skill.Source, skill.Kind, skill.Enabled, skill.Revision,
                    skill.Aliases, skill.AllowedTools, Content = withContent ? skill.Content : null
                }).ToArray(), cursor, limit, reply.Json);
            }
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
