namespace AI.Client.Mcp.App;

using AI.Client.Application.Chats;
using AI.Client.Application.Runs;
using AI.Client.Contracts.Chats;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

public enum ChatOperation
{
    /// <summary>Start a new chat in a project. Needs 'title'; 'revision' is ignored.</summary>
    Create,

    /// <summary>Change a chat's title. Needs 'chatId', 'title' and 'revision'.</summary>
    Rename,

    /// <summary>Pin or unpin a chat in the sidebar. Needs 'chatId', 'isPinned' and 'revision'.</summary>
    Pin,

    /// <summary>Point a chat at a connection, or clear it when 'connectionId' is null. Needs 'chatId' and 'revision'.</summary>
    SetEndpoint,

    /// <summary>Change a branch's title. Needs 'chatId', 'branchId', 'title' and 'revision'.</summary>
    RenameBranch,

    /// <summary>Delete a chat and its whole history. Needs 'chatId' and 'revision'. Honours 'dryRun'.</summary>
    Delete,

    /// <summary>Delete one branch and the messages only it reaches. Needs 'chatId', 'branchId' and 'revision'. Honours 'dryRun'.</summary>
    DeleteBranch,
}

/// <remarks>
/// The dispatcher arrives as a factory rather than an instance: it owns the agent, the agent owns
/// the tool session, and the session owns this tool. Resolving it on use instead of on construction
/// is what keeps that loop from having to be built all at once — every tool here does the same.
/// </remarks>
[McpServerToolType]
public sealed class AppChatsTool(IChatService chats, Func<IChatRunDispatcher> runs, AppWrites writes) : IAppTool
{
    public McpServerTool Create() => McpServerTool.Create(
        ChatsAsync,
        new McpServerToolCreateOptions
        {
            SerializerOptions = ToolReply.Json,
            Description = "Create and change this application's chats and branches. Read the chat with 'app_read' first and pass the "
                          + "'revision' you saw: a stale revision changes nothing and reports the current one back. 'operationId' must "
                          + "be a fresh UUID per distinct change, and the same UUID when repeating one that may already have landed. "
                          + "Only 'Delete' and 'DeleteBranch' understand 'dryRun', and they rehearse by default: they describe "
                          + "what they would do and change nothing until 'dryRun' is false. Every other operation applies straight "
                          + "away and rejects 'dryRun: true' rather than quietly ignoring it."
        });

    [McpServerTool(Name = "app_chats", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(AppWriteResult))]
    private Task<CallToolResult> ChatsAsync(
        ChatOperation operation,
        Guid projectId,
        Guid operationId,
        Guid? chatId = null,
        Guid? branchId = null,
        string? title = null,
        bool isPinned = false,
        Guid? connectionId = null,
        long revision = 0,
        bool? dryRun = null,
        CancellationToken cancellationToken = default) =>
        writes.RunAsync(operation.ToString(), operationId, builder => operation switch
        {
            // Unspecified means "the operation's own default", which is a rehearsal for the two
            // that can delete and nothing at all for the rest. Asking for one where it cannot
            // happen is refused rather than ignored: silently applying a change the caller
            // believed it was only rehearsing is the worst of the three outcomes.
            _ when dryRun == true && operation is not (ChatOperation.Delete or ChatOperation.DeleteBranch) =>
                throw new ArgumentException(
                    $"'{operation}' cannot be rehearsed; only Delete and DeleteBranch honour 'dryRun'.", nameof(dryRun)),
            ChatOperation.Create => CreateAsync(builder, projectId, title, connectionId, cancellationToken),
            ChatOperation.Rename => RenameAsync(builder, projectId, chatId, title, revision, cancellationToken),
            ChatOperation.Pin => PinAsync(builder, projectId, chatId, isPinned, revision, cancellationToken),
            ChatOperation.SetEndpoint => SetEndpointAsync(builder, projectId, chatId, connectionId, revision, cancellationToken),
            ChatOperation.RenameBranch => RenameBranchAsync(builder, projectId, chatId, branchId, title, revision, cancellationToken),
            ChatOperation.Delete => DeleteAsync(builder, projectId, chatId, revision, dryRun ?? true, cancellationToken),
            ChatOperation.DeleteBranch => DeleteBranchAsync(builder, projectId, chatId, branchId, revision, dryRun ?? true, cancellationToken),
            _ => throw new ArgumentException("Unknown operation.", nameof(operation)),
        });

    private async Task<AppWriteResult> CreateAsync(
        AppWriteBuilder builder, Guid projectId, string? title, Guid? connectionId, CancellationToken cancellationToken)
    {
        var chat = await chats.CreateAsync(projectId, new CreateChatRequest(Text(title, nameof(title)), connectionId), cancellationToken);
        return builder.Applied($"Created chat '{chat.Title}'.", projectId, chat.Id, revision: chat.Revision,
            current: Element(chat with { Messages = [] }));
    }

    private Task<AppWriteResult> RenameAsync(
        AppWriteBuilder builder, Guid projectId, Guid? chatId, string? title, long revision, CancellationToken cancellationToken) =>
        ChangeAsync(builder, projectId, chatId,
            id => chats.RenameAsync(projectId, id, new RenameChatRequest(Text(title, nameof(title)), revision), cancellationToken),
            chat => $"Renamed the chat to '{chat.Title}'.", cancellationToken);

    private Task<AppWriteResult> PinAsync(
        AppWriteBuilder builder, Guid projectId, Guid? chatId, bool isPinned, long revision, CancellationToken cancellationToken) =>
        ChangeAsync(builder, projectId, chatId,
            async id =>
            {
                var summary = await chats.PinAsync(projectId, id, new PinChatRequest(isPinned, revision), cancellationToken);
                return summary is null ? null : await chats.GetAsync(projectId, id, cancellationToken);
            },
            _ => isPinned ? "Pinned the chat." : "Unpinned the chat.", cancellationToken);

    private Task<AppWriteResult> SetEndpointAsync(
        AppWriteBuilder builder, Guid projectId, Guid? chatId, Guid? connectionId, long revision, CancellationToken cancellationToken) =>
        ChangeAsync(builder, projectId, chatId,
            id => chats.UpdateEndpointAsync(projectId, id, new UpdateChatEndpointRequest(connectionId, revision), cancellationToken),
            _ => connectionId is { } id ? $"Pointed the chat at connection {id}." : "Cleared the chat's connection.", cancellationToken);

    private Task<AppWriteResult> RenameBranchAsync(
        AppWriteBuilder builder, Guid projectId, Guid? chatId, Guid? branchId, string? title, long revision,
        CancellationToken cancellationToken) =>
        ChangeAsync(builder, projectId, chatId,
            id => chats.RenameBranchAsync(projectId, id, Required(branchId, nameof(branchId)),
                new RenameChatBranchRequest(Text(title, nameof(title)), revision), cancellationToken),
            _ => $"Renamed the branch to '{title}'.", cancellationToken);

    private async Task<AppWriteResult> DeleteAsync(
        AppWriteBuilder builder, Guid projectId, Guid? chatId, long revision, bool dryRun, CancellationToken cancellationToken)
    {
        var id = Required(chatId, nameof(chatId));
        var chat = await chats.GetAsync(projectId, id, cancellationToken)
            ?? throw new InvalidOperationException("Chat not found.");
        if (dryRun)
            return builder.Planned(
                $"Would delete chat '{chat.Title}' with {chat.Messages.Count} message(s) and {chat.Branches?.Count ?? 0} branch(es).",
                projectId, id, revision: chat.Revision, current: Element(chat with { Messages = [] }));
        var result = await runs().DeleteChatAsync(projectId, id, revision, cancellationToken);
        return result.IsDeleted
            ? builder.Applied($"Deleted chat '{chat.Title}'.", projectId, id, revision: result.Revision)
            : builder.Conflict(result.Revision, Element(chat with { Messages = [] }), projectId, id);
    }

    private async Task<AppWriteResult> DeleteBranchAsync(
        AppWriteBuilder builder, Guid projectId, Guid? chatId, Guid? branchId, long revision, bool dryRun,
        CancellationToken cancellationToken)
    {
        var id = Required(chatId, nameof(chatId));
        var branch = Required(branchId, nameof(branchId));
        var chat = await chats.GetAsync(projectId, id, cancellationToken)
            ?? throw new InvalidOperationException("Chat not found.");
        var title = chat.Branches?.SingleOrDefault(item => item.Id == branch)?.Title
            ?? throw new InvalidOperationException("Branch not found.");
        if (dryRun)
            return builder.Planned($"Would delete branch '{title}' and every message only it reaches.",
                projectId, id, branch, chat.Revision, Element(chat with { Messages = [] }));
        var result = await runs().DeleteBranchAsync(projectId, id, branch, revision, cancellationToken);
        return result.IsDeleted
            ? builder.Applied($"Deleted branch '{title}'.", projectId, id, branch, revision: result.Revision)
            : builder.Conflict(result.Revision, Element(chat with { Messages = [] }), projectId, id);
    }

    /// <summary>
    /// Chat writes answer null for both "no such chat" and "someone wrote first", so the chat is
    /// re-read to tell the two apart before anything is reported back.
    /// </summary>
    private async Task<AppWriteResult> ChangeAsync(
        AppWriteBuilder builder, Guid projectId, Guid? chatId,
        Func<Guid, Task<ChatDetails?>> change, Func<ChatDetails, string> effect, CancellationToken cancellationToken)
    {
        var id = Required(chatId, nameof(chatId));
        var updated = await change(id);
        if (updated is not null)
            return builder.Applied(effect(updated), projectId, id, revision: updated.Revision,
                current: Element(updated with { Messages = [] }));
        var current = await chats.GetAsync(projectId, id, cancellationToken);
        return current is null
            ? builder.Failed("Chat not found.", projectId, id)
            : builder.Conflict(current.Revision, Element(current with { Messages = [] }), projectId, id);
    }

    private static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, ToolReply.Json);

    private static Guid Required(Guid? value, string name) =>
        value ?? throw new ArgumentException($"'{name}' is required for this operation.", name);

    private static string Text(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"'{name}' is required for this operation.", name) : value;
}
