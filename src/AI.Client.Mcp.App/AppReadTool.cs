namespace AI.Client.Mcp.App;

using AI.Client.Application.Chats;
using AI.Client.Application.Projects;
using AI.Client.Application.Runs;
using AI.Client.Application.Settings;
using AI.Client.Contracts.Chats;
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
}

[McpServerToolType]
public sealed class AppReadTool(
    IProjectService projects,
    IChatService chats,
    IGlobalSettingsService settings,
    Func<IChatRunDispatcher> runs) : IAppTool
{
    public McpServerTool Create() => McpServerTool.Create(
        ReadAsync,
        new McpServerToolCreateOptions
        {
            SerializerOptions = ToolReply.Json,
            Description = "Read this application's own data: projects, chats, messages, runs and global settings. "
                          + "'Project', 'Chat' and 'Messages' need the ids named in their description; the others ignore them. "
                          + "Results are paged: pass the returned 'nextCursor' back to continue, and expect 'truncated' when a page "
                          + "ended on its character budget rather than on 'limit'. API keys are never returned — a connection only "
                          + "reports whether it has one. Call this before any change, because every mutating tool here needs the "
                          + "current revision of what it is changing."
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
        CancellationToken cancellationToken = default)
    {
        try
        {
            return ToolReply.Of(await PageAsync(resource, projectId, chatId, branchId, cursor, limit, cancellationToken));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return ToolReply.Of(new AppReadResult(resource.ToString(), [], null, false, 0, 0, error.Message), true);
        }
    }

    private async Task<AppReadResult> PageAsync(
        AppResource resource, Guid? projectId, Guid? chatId, Guid? branchId, string? cursor, int limit,
        CancellationToken cancellationToken)
    {
        switch (resource)
        {
            case AppResource.Projects:
                return Paging.Page("Projects", await projects.ListAsync(cancellationToken), cursor, limit);
            case AppResource.Project:
            {
                var project = await projects.GetAsync(Required(projectId, nameof(projectId)), cancellationToken)
                    ?? throw new InvalidOperationException("Project not found.");
                return Paging.Page("Project", [project], cursor, limit);
            }
            case AppResource.Chats:
                return Paging.Page("Chats", await chats.ListAsync(Required(projectId, nameof(projectId)), cancellationToken), cursor, limit);
            case AppResource.Chat:
            {
                var chat = await LoadChatAsync(projectId, chatId, cancellationToken);
                // Messages are their own resource: a chat read would otherwise always drag the
                // whole transcript along, and no caller asking for a title wants that.
                return Paging.Page("Chat", [chat with { Messages = [] }], cursor, limit);
            }
            case AppResource.Messages:
            {
                var chat = await LoadChatAsync(projectId, chatId, cancellationToken);
                return Paging.Page("Messages", Branch(chat, branchId), cursor, limit);
            }
            case AppResource.Runs:
            {
                var snapshots = await runs().GetSnapshotAsync(cancellationToken);
                var filtered = snapshots
                    .Where(run => projectId is not { } project || run.ProjectId == project)
                    .Where(run => chatId is not { } chat || run.ChatId == chat)
                    .Where(run => branchId is not { } branch || run.BranchId == branch)
                    .ToArray();
                return Paging.Page("Runs", filtered, cursor, limit);
            }
            case AppResource.Settings:
                return Paging.Page("Settings", [await settings.GetAsync(cancellationToken)], cursor, limit);
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
