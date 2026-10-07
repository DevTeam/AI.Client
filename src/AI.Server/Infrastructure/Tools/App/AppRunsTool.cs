namespace AI.Mcp.App;

using AI.Application.Runs;
using AI.Application.Tools;
using AI.Contracts.Runs;
using AI.Contracts.Chats;
using AI.Contracts.Resources;
using AI.Domain.Chats;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

public enum RunOperation
{
    /// <summary>Put a message into a chat and let it run. Needs 'content'.</summary>
    Submit,

    /// <summary>Stop the run on a branch, cancelling generation or a pending approval.</summary>
    Stop,

    /// <summary>Change a queued message's text or position. Needs 'messageId', plus 'content' or 'position'.</summary>
    UpdateQueued,

    /// <summary>Drop one queued message. Needs 'messageId'.</summary>
    RemoveQueued,

    /// <summary>Drop every queued message on a branch.</summary>
    ClearQueue,

    /// <summary>Continue a run that was interrupted or paused.</summary>
    Resume,

    /// <summary>Discard the failed message at the head of the queue and continue.</summary>
    SkipFailed,

    /// <summary>Re-anchor the queue on the branch's current head.</summary>
    Rebase,

    /// <summary>Clear the chat's unread marker.</summary>
    MarkRead,
}

/// <summary>How a submitted message joins the chat, mirroring what the composer offers a person.</summary>
public enum SubmitMode
{
    /// <summary>Append to the branch head and run now.</summary>
    Send,

    /// <summary>Append to the branch head and wait behind whatever is already queued.</summary>
    Queue,

    /// <summary>Start a new branch from 'parentMessageId'; 'title' names it, so no rename is needed.</summary>
    Fork,

    /// <summary>Replace 'parentMessageId' and everything below it.</summary>
    Replace,

    /// <summary>
    /// Add to the branch without starting a turn: a running turn reads it at its next step, an idle
    /// branch just records it. For information that needs no answer of its own.
    /// </summary>
    Aside,
}

/// <summary>What a message to another branch is meant to be; the receiving model reads it in the message header.</summary>
public enum MessageIntent
{
    /// <summary>Needs an answer before the sender can continue.</summary>
    Question,

    /// <summary>Answers a question.</summary>
    Answer,

    /// <summary>Fixes something the others must follow.</summary>
    Decision,

    /// <summary>Progress, nothing to act on; send it as an Aside.</summary>
    Status,

    /// <summary>The sender is stuck and cannot continue on its own.</summary>
    Blocker,

    /// <summary>The sender's part is finished and the message is its result.</summary>
    Done,
}

[McpServerToolType]
public sealed class AppRunsTool(Func<IChatRunDispatcher> runs, IAppWrites writes, IAppToolReply reply) : IAppTool
{
    /// <summary>Anything longer than this belongs in the chat, not in a wait inside one tool call.</summary>
    private const int MaxWaitMs = 600_000;

    private const int MinWaitMs = 1_000;

    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => new Session(this, run, reply).Create();

    private sealed class Session(AppRunsTool tool, ToolRunContext run, IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(
            RunsAsync,
            new McpServerToolCreateOptions
            {
                SerializerOptions = reply.Json,
                Description = "Drive this application's chat runs: put a message into any chat — including one you just created — and manage "
                              + "its queue. 'branchId' defaults to the chat's main branch, whose id equals the chat's. With 'wait' false the "
                              + "call returns as soon as the message is accepted and the answer is read later with 'app_read'; with 'wait' "
                              + "true it returns when that run stops, or reports the run's current status if 'waitTimeoutMs' runs out first. "
                              + "Waiting is also bounded by this tool's own policy timeout, so a long wait can be cut short from outside. "
                              + "'operationId' must be a fresh UUID per distinct message and the same UUID when repeating one. "
                              + "Submit may include resource references returned by app_resources Create; these add no file contents to the message. "
                              + "A submitted message is marked as sent by this run's branch, and 'intent' says what it is to the branch "
                              + "that reads it. Mode Aside starts no turn: use it for status and other information that needs no answer. "
                              + "A Fork for a teammate takes 'memberName' and 'role' instead of 'title': the branch is named \"Name · Role\" and "
                              + "every message from it is signed that way."
            });

        [McpServerTool(Name = "app_runs", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false,
            UseStructuredContent = true, OutputSchemaType = typeof(AppWriteResult))]
        private Task<CallToolResult> RunsAsync(
            RunOperation operation,
            Guid projectId,
            Guid chatId,
            Guid operationId,
            Guid? branchId = null,
            string? content = null,
            SubmitMode mode = SubmitMode.Send,
            MessageIntent? intent = null,
            string? title = null,
            string? memberName = null,
            string? role = null,
            Guid? parentMessageId = null,
            Guid? messageId = null,
            ChatResource[]? resources = null,
            int? position = null,
            bool wait = false,
            int waitTimeoutMs = 60_000,
            CancellationToken cancellationToken = default) =>
            tool.RunsAsync(run, operation, projectId, chatId, operationId, branchId, content, mode, intent, title, memberName, role, parentMessageId,
                messageId, resources, position, wait, waitTimeoutMs, cancellationToken);
    }

    private Task<CallToolResult> RunsAsync(
        ToolRunContext run,
        RunOperation operation,
        Guid projectId,
        Guid chatId,
        Guid operationId,
        Guid? branchId,
        string? content,
        SubmitMode mode,
        MessageIntent? intent,
        string? title,
        string? memberName,
        string? role,
        Guid? parentMessageId,
        Guid? messageId,
        ChatResource[]? resources,
        int? position,
        bool wait,
        int waitTimeoutMs,
        CancellationToken cancellationToken)
    {
        // The main branch carries the chat's own id, so an omitted branch means "the chat itself".
        var branch = branchId ?? chatId;
        // A run outside any chat (a subtask) has no branch to answer to, so its messages carry no sender.
        var sender = run.ChatId == Guid.Empty ? null
            : new ChatMessageSender(run.ChatId, run.BranchId, intent?.ToString().ToLowerInvariant());
        return writes.RunAsync(operation.ToString(), operationId, builder => operation switch
        {
            RunOperation.Submit => SubmitAsync(builder, projectId, chatId, branch, operationId, content, resources, mode,
                sender, title, memberName, role, parentMessageId, wait, waitTimeoutMs, cancellationToken),
            RunOperation.Stop => CommandAsync(builder, projectId, chatId, branch, "Stopped the run.",
                () => runs().StopAsync(projectId, chatId, branch, cancellationToken, operationId)),
            RunOperation.UpdateQueued => CommandAsync(builder, projectId, chatId, branch, "Updated the queued message.",
                () => runs().UpdateQueuedAsync(projectId, chatId, branch, Required(messageId, nameof(messageId)),
                    new UpdateQueuedMessageRequest(operationId, content, position), cancellationToken)),
            RunOperation.RemoveQueued => CommandAsync(builder, projectId, chatId, branch, "Removed the queued message.",
                () => runs().RemoveQueuedAsync(projectId, chatId, branch, Required(messageId, nameof(messageId)), cancellationToken, operationId)),
            RunOperation.ClearQueue => CommandAsync(builder, projectId, chatId, branch, "Cleared the queue.",
                () => runs().ClearAsync(projectId, chatId, branch, cancellationToken, operationId)),
            RunOperation.Resume => CommandAsync(builder, projectId, chatId, branch, "Resumed the run.",
                () => runs().ResumeAsync(projectId, chatId, branch, cancellationToken, operationId)),
            RunOperation.SkipFailed => CommandAsync(builder, projectId, chatId, branch, "Skipped the failed message.",
                () => runs().SkipFailedAsync(projectId, chatId, branch, cancellationToken, operationId)),
            RunOperation.Rebase => CommandAsync(builder, projectId, chatId, branch, "Rebased the queue on the branch head.",
                () => runs().RebaseAsync(projectId, chatId, branch, cancellationToken, operationId)),
            RunOperation.MarkRead => CommandAsync(builder, projectId, chatId, branch, "Marked the chat read.",
                () => runs().MarkReadAsync(projectId, chatId, branch, cancellationToken, operationId)),
            _ => throw new ArgumentException("Unknown operation.", nameof(operation)),
        });
    }

    private async Task<AppWriteResult> SubmitAsync(
        AppWriteBuilder builder, Guid projectId, Guid chatId, Guid branchId, Guid operationId, string? content,
        ChatResource[]? resources, SubmitMode mode, ChatMessageSender? sender, string? title,
        string? memberName, string? role, Guid? parentMessageId, bool wait, int waitTimeoutMs, CancellationToken cancellationToken)
    {
        var text = content ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) && resources is not { Length: > 0 })
            throw new ArgumentException("'content' or 'resources' is required to submit a message.", nameof(content));
        if (mode is SubmitMode.Fork or SubmitMode.Replace && parentMessageId is null)
            throw new ArgumentException("'parentMessageId' is required to fork or replace.", nameof(parentMessageId));
        if (string.IsNullOrWhiteSpace(memberName) != string.IsNullOrWhiteSpace(role) || memberName is not null && mode != SubmitMode.Fork)
            throw new ArgumentException("'memberName' and 'role' go together, and only with mode Fork.", nameof(memberName));
        if (mode == SubmitMode.Aside && parentMessageId is not null)
            throw new ArgumentException("An aside always joins the branch head; omit 'parentMessageId'.", nameof(parentMessageId));

        // The dispatcher dedupes by operation id too, and reusing it as the message id keeps a
        // repeated call from writing a second message with a different identity.
        var request = new SubmitChatMessageRequest(operationId, operationId, text,
            Enum.Parse<ChatSubmitMode>(mode.ToString()), branchId,
            parentMessageId is null ? MessageParentMode.BranchHead : MessageParentMode.Explicit, parentMessageId,
            mode == SubmitMode.Replace ? parentMessageId : null, Resources: resources,
            BranchTitle: mode == SubmitMode.Fork && !string.IsNullOrWhiteSpace(title) ? title.Trim() : null,
            BranchMember: mode == SubmitMode.Fork && !string.IsNullOrWhiteSpace(memberName)
                ? new TeamMember(memberName.Trim(), role?.Trim() ?? string.Empty) : null);
        var snapshot = sender is null
            ? await runs().SubmitAsync(projectId, chatId, request, cancellationToken)
            : await runs().SubmitFromRunAsync(projectId, chatId, request, sender, cancellationToken);
        // An aside starts nothing, so there is nothing of its own to wait for.
        if (mode == SubmitMode.Aside)
            return builder.Applied(snapshot.Queue.Any(item => item.Id == operationId)
                    ? "Queued the aside; the turn in flight reads it at its next step, or it follows the reply."
                    : "Added the aside to the branch; no turn was started.", projectId, chatId, snapshot.BranchId, operationId,
                snapshot.ChatRevision, snapshot.Status.ToString(), Element(snapshot, reply.Json));
        if (wait) snapshot = await AwaitStopAsync(snapshot, waitTimeoutMs, cancellationToken);
        return builder.Applied(Effect(snapshot, wait), projectId, chatId, snapshot.BranchId, operationId,
            snapshot.ChatRevision, snapshot.Status.ToString(), Element(snapshot, reply.Json));
    }

    private static string Effect(ChatRunSnapshot snapshot, bool waited)
    {
        var effect = waited
            ? $"Submitted the message and waited; the run is {snapshot.Status}."
            : $"Submitted the message; the run is {snapshot.Status}.";
        return snapshot.PendingApproval is { } approval
            ? effect + $" It is waiting for a person to confirm '{approval.Name}'."
            : effect;
    }

    /// <summary>
    /// Follows the run until it stops on its own, needs a person, or the caller's patience runs
    /// out. A timeout is not a failure: the message was accepted either way, so the last known
    /// status is reported instead of an error.
    /// </summary>
    private async Task<ChatRunSnapshot> AwaitStopAsync(ChatRunSnapshot start, int waitTimeoutMs, CancellationToken cancellationToken)
    {
        var latest = start;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(waitTimeoutMs, MinWaitMs, MaxWaitMs)));
        var started = false;
        try
        {
            await foreach (var snapshots in runs().SubscribeAsync(timeout.Token))
            {
                if (snapshots.SingleOrDefault(run => run.ChatId == start.ChatId && run.BranchId == start.BranchId) is not { } run) continue;
                latest = run;
                // A status carried over from before this message was submitted says nothing about
                // it. Only a state the run reached afterwards counts, and the run's own revision is
                // what distinguishes the two — without this a chat that answered a minute ago
                // reports an immediate, entirely fictional "Completed".
                if (run.Revision <= start.Revision) continue;
                if (run.Status == ChatRunStatus.Generating) { started = true; continue; }
                // Idle without ever generating means the dispatcher has not picked the message up
                // yet; that is not something to report as finished either.
                if (run.Status != ChatRunStatus.Idle || (started && run.Queue.Count == 0)) return run;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The wait expired, which the caller reads from the status it gets back.
        }

        return latest;
    }

    private async Task<AppWriteResult> CommandAsync(
        AppWriteBuilder builder, Guid projectId, Guid chatId, Guid branchId, string effect, Func<Task<ChatRunSnapshot?>> command)
    {
        var snapshot = await command();
        if (snapshot is null) return builder.Failed("No run exists for that chat and branch.", projectId, chatId);
        // Reporting plain success while the run is still stuck behind a confirmation is what makes
        // a caller retry the same command forever. Say what is actually holding it.
        var blocked = snapshot.PendingApproval is { } approval
            ? $" The run is still waiting for a person to confirm '{approval.Name}'."
            : string.Empty;
        return builder.Applied(effect + blocked, projectId, chatId, snapshot.BranchId, revision: snapshot.ChatRevision,
            status: snapshot.Status.ToString(), current: Element(snapshot, reply.Json));
    }

    private static JsonElement Element<T>(T value, JsonSerializerOptions options) => JsonSerializer.SerializeToElement(value, options);

    private static Guid Required(Guid? value, string name) =>
        value ?? throw new ArgumentException($"'{name}' is required for this operation.", name);
}
