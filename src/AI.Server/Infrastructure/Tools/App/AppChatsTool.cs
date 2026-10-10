namespace AI.Mcp.App;

using AI.Application.Chats;
using AI.Application.Runs;
using AI.Application.Tools;
using AI.Contracts.Chats;
using AI.Contracts.Runs;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

public enum ChatOperation
{
    /// <summary>Start a new chat in a project. Needs 'title'; 'revision' is ignored.</summary>
    Create,

    /// <summary>Archive one chat, preserving history and pin order.</summary>
    Archive,
    /// <summary>Restore one chat from the archive.</summary>
    Restore,
    /// <summary>Preview old chats by activityBefore; apply explicit targets with dryRun false after user confirmation.</summary>
    ArchiveBatch,
    /// <summary>Restore chats archived by archiveOperationId.</summary>
    UndoArchive,

    /// <summary>Change a chat's title. Needs 'chatId' and 'title'; a stale 'revision' does not block a rename.</summary>
    Rename,

    /// <summary>Pin or unpin a chat in the sidebar. Needs 'chatId', 'isPinned' and 'revision'.</summary>
    Pin,

    /// <summary>Point a chat at a connection, or clear it when 'connectionId' is null. Needs 'chatId' and 'revision'.</summary>
    SetEndpoint,

    /// <summary>Change a branch's title. Needs 'chatId', 'branchId', 'title' and 'revision'.</summary>
    RenameBranch,

    /// <summary>
    /// Override or inherit a branch's settings from 'branchSettings'. 'chatId' and 'branchId' default to the
    /// current branch; 'revision' is not needed.
    /// </summary>
    SetBranchSettings,

    /// <summary>Delete a chat and its whole history. Needs 'chatId' and 'revision'. Honours 'dryRun'.</summary>
    Delete,

    /// <summary>Delete one branch and the messages only it reaches. Needs 'chatId', 'branchId' and 'revision'. Honours 'dryRun'.</summary>
    DeleteBranch,
}

/// <remarks>
/// The dispatcher arrives as a factory rather than an instance: it owns the agent, the agent owns
/// the tool session, and the session owns this tool. Resolving it on use instead of on construction
/// is what keeps that loop from having to be built all at once � every tool here does the same.
/// </remarks>
[McpServerToolType]
public sealed class AppChatsTool(IChatService chats, Func<IChatRunDispatcher> runs, IAppWrites writes, IAppToolReply reply, IChatArchiveService archive, Func<IUserPromptBroker> prompts) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => new Session(this, run, reply).Create();

    private sealed class Session(AppChatsTool tool, ToolRunContext run, IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(
            ChatsAsync,
            new McpServerToolCreateOptions
            {
                SerializerOptions = reply.Json,
                Description = "Create and change this application's chats and branches. Read the chat with 'app_read' first and pass the "
                              + "'revision' you saw: a stale revision changes nothing and reports the current one back. 'operationId' must "
                              + "be a fresh UUID per distinct change, and the same UUID when repeating one that may already have landed. "
                              + "Archive and Restore change one explicitly named chat using its revision, even while running; archiving does not stop it. ArchiveBatch previews chats before activityBefore "
                              + "(an ISO timestamp with time zone offset), excluding pinned unless includePinned is true. If the project or "
                              + "cutoff is ambiguous, ask_user first. Apply the preview's explicit targets with dryRun false; the tool asks "
                              + "the user to confirm and does nothing without an affirmative answer. Supply confirmationText, confirmLabel and cancelLabel in the user's language.  UndoArchive restores exactly the "
                              + "chats still marked with archiveOperationId, including after restart. "
                              + "Only 'Delete', 'DeleteBranch' and 'ArchiveBatch' understand 'dryRun', and they rehearse by default: they describe "
                              + "what they would do and change nothing until 'dryRun' is false. Every other operation applies straight "
                              + "away and rejects 'dryRun: true' rather than quietly ignoring it. "
                              + "SetBranchSettings: a branch inherits its connection (model), tool approval mode and tool policies from "
                              + "its parent branch, and the main branch's are the chat's own. In 'branchSettings' each of 'connection' "
                              + "(a connection id) and 'approvalMode' (Ask, Auto, FullAccess) is left as it is when omitted, overridden "
                              + "on this branch when given a value, and inherited again when given 'inherit'; 'inheritAll' true drops every "
                              + "override of the branch, tool policies included. The main branch has nothing to inherit from: there "
                              + "'connection' 'inherit' follows the project default and 'approvalMode' needs a value. Read the effective "
                              + "values and where they come from with app_read. Change approvals or a branch's model only when the user "
                              + "asked for it. Branch tool policies belong to app_security SetBranchToolPolicy. Schedules are per branch "
                              + "and never inherited (app_schedule)."
            });

        [McpServerTool(Name = "app_chats", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false,
            UseStructuredContent = true, OutputSchemaType = typeof(AppWriteResult))]
        private Task<CallToolResult> ChatsAsync(
            ChatOperation operation, Guid projectId, Guid operationId,
            Guid? chatId = null, Guid? branchId = null, string? title = null,
            bool isPinned = false, Guid? connectionId = null, long revision = 0, bool? dryRun = null,
            DateTimeOffset? activityBefore = null, bool includePinned = false,
            ChatArchiveTarget[]? targets = null, Guid? archiveOperationId = null,
            string? confirmationText = null, string? confirmLabel = null, string? cancelLabel = null,
            BranchSettingsPayload? branchSettings = null,
            CancellationToken cancellationToken = default) =>
            tool.ChatsAsync(run, operation, projectId, operationId, chatId, branchId, title, isPinned, connectionId,
                revision, dryRun, activityBefore, includePinned, targets, archiveOperationId, confirmationText, confirmLabel, cancelLabel,
                branchSettings, cancellationToken);
    }

    private Task<CallToolResult> ChatsAsync(
        ToolRunContext run,
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
        DateTimeOffset? activityBefore = null, bool includePinned = false,
        ChatArchiveTarget[]? targets = null, Guid? archiveOperationId = null,
        string? confirmationText = null, string? confirmLabel = null, string? cancelLabel = null,
        BranchSettingsPayload? branchSettings = null,
        CancellationToken cancellationToken = default) =>
        writes.RunAsync(operation.ToString(), operationId, builder => operation switch
        {
            // Unspecified means "the operation's own default", which is a rehearsal for the three
            // that can delete and nothing at all for the rest. Asking for one where it cannot
            // happen is refused rather than ignored: silently applying a change the caller
            // believed it was only rehearsing is the worst of the three outcomes.
            _ when dryRun == true && operation is not (ChatOperation.Delete or ChatOperation.DeleteBranch or ChatOperation.ArchiveBatch) =>
                throw new ArgumentException(
                    $"'{operation}' cannot be rehearsed; only Delete, DeleteBranch and ArchiveBatch honour 'dryRun'.", nameof(dryRun)),
            ChatOperation.Archive => ArchiveAsync(builder, projectId, operationId, chatId, revision, true, cancellationToken),
            ChatOperation.Restore => ArchiveAsync(builder, projectId, operationId, chatId, revision, false, cancellationToken),
            ChatOperation.ArchiveBatch => ArchiveBatchAsync(builder, run, projectId, operationId, activityBefore, includePinned, targets, dryRun ?? true, confirmationText, confirmLabel, cancelLabel, cancellationToken),
            ChatOperation.UndoArchive => UndoArchiveAsync(builder, projectId, archiveOperationId, cancellationToken),
            ChatOperation.Create => CreateAsync(builder, projectId, title, connectionId, cancellationToken),
            ChatOperation.Rename => RenameAsync(builder, projectId, chatId, title, revision, cancellationToken),
            ChatOperation.Pin => PinAsync(builder, projectId, chatId, isPinned, revision, cancellationToken),
            ChatOperation.SetEndpoint => SetEndpointAsync(builder, projectId, chatId, connectionId, revision, cancellationToken),
            ChatOperation.RenameBranch => RenameBranchAsync(builder, projectId, chatId, branchId, title, revision, cancellationToken),
            ChatOperation.SetBranchSettings => SetBranchSettingsAsync(builder, projectId, chatId ?? run.ChatId,
                branchId ?? (chatId is null || chatId == run.ChatId ? run.BranchId : chatId.Value), branchSettings, cancellationToken),
            ChatOperation.Delete => DeleteAsync(builder, projectId, chatId, revision, dryRun ?? true, cancellationToken),
            ChatOperation.DeleteBranch => DeleteBranchAsync(builder, projectId, chatId, branchId, revision, dryRun ?? true, cancellationToken),
            _ => throw new ArgumentException("Unknown operation.", nameof(operation)),
        });

    private async Task<AppWriteResult> ArchiveAsync(AppWriteBuilder builder, Guid projectId, Guid operationId,
        Guid? chatId, long revision, bool archived, CancellationToken token)
    {
        var result = await archive.ApplyAsync(projectId,
            new ChatArchiveRequest(archived, operationId, [new(Required(chatId, nameof(chatId)), revision)], SkipBusy: false), token);
        return ArchiveReply(builder, projectId, result);
    }

    private async Task<AppWriteResult> ArchiveBatchAsync(AppWriteBuilder builder, ToolRunContext run,
        Guid projectId, Guid operationId, DateTimeOffset? before, bool includePinned,
        ChatArchiveTarget[]? targets, bool dryRun, string? confirmationText, string? confirmLabel, string? cancelLabel, CancellationToken token)
    {
        if (dryRun)
        {
            var preview = await archive.PreviewAsync(projectId,
                new ChatArchivePreviewRequest(before ?? throw new ArgumentException("Specify activityBefore, including its time zone offset."), includePinned), token);
            return builder.Planned($"Found {preview.Chats.Count} eligible chats; {preview.Skipped} need attention. "
                + "Pass the selected chat ids and revisions as targets to apply this preview.", projectId,
                current: Element(preview, reply.Json));
        }
        if (targets is not { Length: > 0 } || targets.Length > 500)
            return builder.Failed("Pass 1 to 500 explicit targets from the preview.", projectId);
        if (!run.Interactive) return builder.Failed("Batch archiving requires an interactive user confirmation.", projectId);
        var listed = (await chats.ListAsync(projectId, token)).Where(chat => targets.Any(target => target.ChatId == chat.Id)).ToArray();
        var names = string.Join(", ", listed.Take(6).Select(chat => chat.Title));
        var answer = await prompts().AskAsync(run, new UserPromptRequest([
            new UserPromptQuestion("archive", (confirmationText is { Length: > 0 and <= 400 } ? confirmationText : "Archive selected chats?")
                + $"\n{targets.DistinctBy(target => target.ChatId).Count()} · {names}",
                "Archive", [new UserPromptOption(confirmLabel is { Length: > 0 and <= 80 } ? confirmLabel : "Archive selected", null), new UserPromptOption(cancelLabel is { Length: > 0 and <= 80 } ? cancelLabel : "Cancel", null)], false, false)
        ]), TimeSpan.FromMinutes(15), token);
        if (answer.Outcome != UserPromptOutcome.Answered
            || !answer.Answers.Any(answer => answer.QuestionId == "archive" && answer.Selected.SequenceEqual([0])))
            return builder.Failed("Archiving was not confirmed. Nothing was changed.", projectId);
        return ArchiveReply(builder, projectId,
            await archive.ApplyAsync(projectId, new ChatArchiveRequest(true, operationId, targets), token));
    }

    private async Task<AppWriteResult> UndoArchiveAsync(AppWriteBuilder builder, Guid projectId, Guid? operationId, CancellationToken token) =>
        ArchiveReply(builder, projectId, await archive.UndoAsync(projectId, Required(operationId, nameof(operationId)), token));

    private AppWriteResult ArchiveReply(AppWriteBuilder builder, Guid projectId, ChatArchiveResult result) =>
        result.Changed.Count > 0
            ? builder.Applied($"Changed {result.Changed.Count} chats; skipped {result.Skipped.Count}. "
                + $"Archive operation: {result.OperationId}.", projectId, current: Element(result, reply.Json))
            : result.Skipped.Count > 0
                ? builder.Failed(string.Join(" ", result.Skipped.Select(item => item.Reason).Distinct()), projectId)
                    with { Current = Element(result, reply.Json) }
                : builder.Planned("No chats needed changing.", projectId, current: Element(result, reply.Json));

    private async Task<AppWriteResult> CreateAsync(
        AppWriteBuilder builder, Guid projectId, string? title, Guid? connectionId, CancellationToken cancellationToken)
    {
        var chat = await chats.CreateAsync(projectId, new CreateChatRequest(Text(title, nameof(title)), connectionId), cancellationToken);
        return builder.Applied($"Created chat '{chat.Title}'.", projectId, chat.Id, revision: chat.Revision,
            current: Element(chat with { Messages = [] }, reply.Json));
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

    private async Task<AppWriteResult> SetBranchSettingsAsync(AppWriteBuilder builder, Guid projectId, Guid chatId,
        Guid branchId, BranchSettingsPayload? payload, CancellationToken cancellationToken)
    {
        var value = payload ?? throw new ArgumentException("'branchSettings' is required for this operation.", nameof(payload));
        var connection = Setting(value.Connection, text => Guid.TryParse(text, out var id) ? id
            : throw new ArgumentException("'connection' must be a connection id or 'inherit'."));
        var mode = Setting(value.ApprovalMode, text =>
            Enum.TryParse<ToolApprovalMode>(text, true, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(text, out _)
                ? parsed : throw new ArgumentException("'approvalMode' must be Ask, Auto, FullAccess or 'inherit'."));
        if (connection is null && mode is null && value.InheritAll != true)
            throw new ArgumentException("Name at least one of 'connection', 'approvalMode' or 'inheritAll'.");
        ChatDetails? updated;
        if (branchId == chatId)
        {
            // The main branch's settings are the chat's own: there is no parent to fall back to.
            if (mode is { Inherit: true } || value.InheritAll == true)
                throw new ArgumentException("The main branch has no parent to inherit from; give 'approvalMode' a value.");
            updated = await chats.GetAsync(projectId, chatId, cancellationToken);
            if (updated is not null && connection is { } chosen)
                updated = await chats.UpdateEndpointAsync(projectId, chatId,
                    new UpdateChatEndpointRequest(chosen.Value, updated.Revision), cancellationToken);
            if (updated is not null && mode?.Value is { } approval)
                updated = await chats.UpdateApprovalModeAsync(projectId, chatId,
                    new UpdateChatApprovalModeRequest(approval), cancellationToken);
        }
        else
        {
            var chat = await chats.GetAsync(projectId, chatId, cancellationToken);
            if (chat is null) return builder.Failed("Chat not found.", projectId, chatId);
            if (chat.Branches?.Any(branch => branch.Id == branchId) != true)
                return builder.Failed("Branch not found.", projectId, chatId);
            updated = await chats.ChangeBranchSettingsAsync(projectId, chatId, branchId, current =>
            {
                var next = value.InheritAll == true ? new BranchSettings() : current;
                if (connection is { } chosen) next = next with { ConnectionId = chosen.Value };
                if (mode is { } approval) next = next with { ApprovalMode = approval.Value };
                return next;
            }, cancellationToken);
        }
        return updated is null
            ? builder.Failed("The chat changed while saving; read it and try again.", projectId, chatId)
            : builder.Applied(value.InheritAll == true ? "The branch inherits every setting again." : "Updated the branch settings.",
                projectId, chatId, revision: updated.Revision, current: Element(updated with { Messages = [] }, reply.Json));
    }

    /// <summary>Null when the field was left out; <c>Inherit</c> when it asks to follow the parent again.</summary>
    private static BranchSetting<T>? Setting<T>(string? text, Func<string, T> parse) where T : struct =>
        string.IsNullOrWhiteSpace(text) ? null
        : string.Equals(text.Trim(), "inherit", StringComparison.OrdinalIgnoreCase) ? new BranchSetting<T>(null, true)
        : new BranchSetting<T>(parse(text.Trim()), false);

    private sealed record BranchSetting<T>(T? Value, bool Inherit) where T : struct;

    private async Task<AppWriteResult> DeleteAsync(
        AppWriteBuilder builder, Guid projectId, Guid? chatId, long revision, bool dryRun, CancellationToken cancellationToken)
    {
        var id = Required(chatId, nameof(chatId));
        var chat = await chats.GetAsync(projectId, id, cancellationToken)
            ?? throw new InvalidOperationException("Chat not found.");
        if (dryRun)
            return builder.Planned(
                $"Would delete chat '{chat.Title}' with {chat.Messages.Count} message(s) and {chat.Branches?.Count ?? 0} branch(es).",
                projectId, id, revision: chat.Revision, current: Element(chat with { Messages = [] }, reply.Json));
        var result = await runs().DeleteChatAsync(projectId, id, revision, cancellationToken);
        return result.IsDeleted
            ? builder.Applied($"Deleted chat '{chat.Title}'.", projectId, id, revision: result.Revision)
            : builder.Conflict(result.Revision, Element(chat with { Messages = [] }, reply.Json), projectId, id);
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
                projectId, id, branch, chat.Revision, Element(chat with { Messages = [] }, reply.Json));
        var result = await runs().DeleteBranchAsync(projectId, id, branch, revision, cancellationToken);
        return result.IsDeleted
            ? builder.Applied($"Deleted branch '{title}'.", projectId, id, branch, revision: result.Revision)
            : builder.Conflict(result.Revision, Element(chat with { Messages = [] }, reply.Json), projectId, id);
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
                current: Element(updated with { Messages = [] }, reply.Json));
        var current = await chats.GetAsync(projectId, id, cancellationToken);
        return current is null
            ? builder.Failed("Chat not found.", projectId, id)
            : builder.Conflict(current.Revision, Element(current with { Messages = [] }, reply.Json), projectId, id);
    }

    private static JsonElement Element<T>(T value, JsonSerializerOptions options) => JsonSerializer.SerializeToElement(value, options);

    private static Guid Required(Guid? value, string name) =>
        value ?? throw new ArgumentException($"'{name}' is required for this operation.", name);

    private static string Text(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"'{name}' is required for this operation.", name) : value;
}

/// <param name="Connection">A connection id to run this branch on, or "inherit"; omitted leaves it as it is.</param>
/// <param name="ApprovalMode">Ask, Auto or FullAccess, or "inherit"; omitted leaves it as it is.</param>
/// <param name="InheritAll">True drops every override of the branch, tool policies included, before the other fields apply.</param>
public sealed record BranchSettingsPayload(string? Connection = null, string? ApprovalMode = null, bool? InheritAll = null);
