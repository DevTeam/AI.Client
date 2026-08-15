using AI.Client.Contracts.Chats;
using AI.Client.Contracts.Runs;
using AI.Client.Web.Chats;
using AI.Client.Web.Runs;

namespace AI.Client.Web.Composer;

/// <summary>
/// Default implementation of <see cref="IChatComposerService"/>. Drives the orchestration by
/// calling the existing chat/runs APIs in the order proven by the integration tests; the only
/// place where new logic lives is the pause/resume reasoning below, which is small enough to
/// read top-to-bottom and pin down with unit tests.
/// </summary>
public sealed class ChatComposerService(
    IChatHistoryApi chatHistory,
    IChatRunsApi chatRuns) : IChatComposerService
{
    // Hard-coded title length kept in sync with Home.razor's CreateChatTitle so the new chat's
    // sidebar label matches what the user sees on send. Single source of truth would mean moving
    // this into ChatDetails or a shared helper, but until a second caller appears the duplication
    // is cheaper than the abstraction.
    private const int NewChatTitleMaxLength = 48;

    public async Task<ComposerSubmitOutcome> SubmitAsync(ComposerSubmitRequest request, CancellationToken cancellationToken)
    {
        if (request.ProjectId is null)
        {
            return new ComposerSubmitOutcome.Rejected("Select a project before sending a message.");
        }
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return new ComposerSubmitOutcome.Rejected("The composer is empty.");
        }
        if (string.IsNullOrWhiteSpace(request.EndpointBaseUrl) || string.IsNullOrWhiteSpace(request.EndpointModel))
        {
            return new ComposerSubmitOutcome.Rejected("Select or configure an endpoint before sending a message.");
        }
        if (request.ReplaceSourceId is not null && request.ReplaceSourceIsGenerating)
        {
            return new ComposerSubmitOutcome.Rejected("Can't replace this message while a response is being generated on this branch.");
        }

        var projectId = request.ProjectId.Value;
        var prompt = request.Message.Trim();

        // Step 1: create the chat if this is a brand-new conversation. We do this BEFORE the
        // pre-pause so a Ctrl+Enter on a fresh project also lands on a paused run — the previous
        // ordering skipped the pause here and the message ran immediately.
        var chat = request.SelectedChat;
        if (chat is null)
        {
            try
            {
                chat = await chatHistory.CreateAsync(
                    projectId,
                    new CreateChatRequest(CreateChatTitle(prompt), request.CredentialProfileId),
                    cancellationToken);
            }
            catch (HttpRequestException)
            {
                return new ComposerSubmitOutcome.Rejected("The endpoint did not accept the request. Verify the base URL, model, and API key.");
            }
            catch (InvalidOperationException)
            {
                return new ComposerSubmitOutcome.Rejected("The endpoint did not accept the request. Verify the base URL, model, and API key.");
            }
        }

        // Step 2: replace-branch semantics. If the user started editing a past message and is
        // now sending, the old branch must be deleted first so the new message can attach to
        // the same parent and become a single replacement.
        if (request.ReplaceSourceId is { } replaceSourceId)
        {
            try
            {
                var replaceSource = chat.Messages.Single(message => message.Id == replaceSourceId);
                var deleteResult = await chatHistory.DeleteBranchAsync(
                    projectId,
                    chat.Id,
                    replaceSourceId,
                    chat.Revision,
                    cancellationToken);
                if (!deleteResult.IsDeleted)
                {
                    return new ComposerSubmitOutcome.Rejected("The branch changed before it could be replaced.");
                }

                var refreshed = await chatHistory.GetAsync(projectId, chat.Id, cancellationToken);
                if (refreshed is null)
                {
                    return new ComposerSubmitOutcome.Rejected("The chat no longer exists.");
                }
                chat = refreshed;
                // Replace-source replaces the leaf with its parent so the freshly enqueued message
                // becomes the new tail of that branch.
                _ = replaceSource.ParentId;
            }
            catch (HttpRequestException)
            {
                return new ComposerSubmitOutcome.Rejected("The endpoint did not accept the request. Verify the base URL, model, and API key.");
            }
            catch (InvalidOperationException)
            {
                return new ComposerSubmitOutcome.Rejected("The endpoint did not accept the request. Verify the base URL, model, and API key.");
            }
        }

        // Step 3: fork anchor. When the composer is in fork mode (either explicit Fork mode —
        // Ctrl+Alt+Enter, with no prior UI action — or any other mode while ForkSourceId is set,
        // the typical state right after EditAndBranch / ForkFromMessage), we want the new
        // message to be a sibling of the current leaf, not a child — that's what creates a new
        // branch in the sidebar tree. So we move both BranchLeafId and ForkSourceId to the
        // leaf's parent before enqueueing.
        //
        // The `request.Mode == ComposerSubmitMode.Fork` half of this condition is NOT redundant
        // with ForkSourceId being set: a bare Ctrl+Alt+Enter on an existing conversation, with no
        // preceding "Fork from here"/"Edit and branch" click, submits Mode=Fork with
        // ForkSourceId still null — without checking Mode here too, that keyboard shortcut
        // silently degraded into a plain Send/append instead of creating a new branch (an
        // earlier version of ForkFromKeyboard computed this anchor itself, in Home.razor, before
        // being extracted into this service — the extraction dropped it).
        //
        // Doing this for Queue mode too is the difference between "queue into a forked session"
        // (sibling-of-leaf) and "queue into the current branch" (child-of-leaf) — the latter
        // would land the queued message on the leaf's own branch, which contradicts the fork the
        // user already initiated.
        var branchLeafId = request.BranchLeafId;
        var forkSourceId = request.ForkSourceId;
        if ((forkSourceId is not null || request.Mode == ComposerSubmitMode.Fork)
            && branchLeafId is { } leafId)
        {
            var leaf = chat.Messages.SingleOrDefault(message => message.Id == leafId);
            if (leaf is not null && leaf.ParentId is { } parentId)
            {
                branchLeafId = parentId;
                forkSourceId = parentId;
            }
        }

        // Step 4: generate ids and decide the run branch. Forking always starts a new run rooted
        // at the new message; other modes reuse the selected branch's run.
        var messageId = Guid.CreateVersion7();
        var runBranchId = forkSourceId is not null
            ? messageId
            : BranchLeafHelpers.RunBranchIdFor(branchLeafId, chat);

        // Step 4b: leave the parent unset when this branch already has pending queue items,
        // instead of anchoring on branchLeafId (the materialized leaf) OR on the last queued
        // item's own id.
        //
        // branchLeafId doesn't advance as items are queued (queued items aren't materialized
        // until the dispatcher processes them), so a naive "always use branchLeafId" would send
        // the SAME ParentMessageId for every queued item.
        //
        // Anchoring on the last queued item's id instead (an earlier, wrong fix) is equally
        // broken the other way: queued.Id is the id the *user* message will get once
        // materialized — it is NOT the id of the assistant's reply to it, which doesn't exist
        // yet. Chaining onto the user-message id makes the next queued message a SIBLING of that
        // still-to-be-generated reply, not its continuation — the exact same "silently forks"
        // symptom, just one hop further down the chain.
        //
        // There is no id we can correctly predict here: the real parent (that reply) is only
        // known once the dispatcher actually generates it. So we send null and let
        // ChatRunDispatcher.ProcessAsync's existing fallback (queued.ParentMessageId ??
        // chat.Messages[^1].Id) resolve it — by the time THIS item is actually processed, the
        // previous queued item has already been materialized and replied to, so
        // chat.Messages[^1] correctly points at that reply.
        var enqueueParentId = branchLeafId;
        if (forkSourceId is null
            && request.SelectedRun is { } selectedRunForQueueChain
            && selectedRunForQueueChain.BranchId == runBranchId
            && selectedRunForQueueChain.Queue.Count > 0)
        {
            enqueueParentId = null;
        }

        // Step 5: pre-pause for Queue mode. The dispatcher auto-starts the worker on Enqueue
        // for Idle/Completed runs and naturally picks the next item up after a Generating run
        // finishes, so without an explicit pause either path would silently run the queued
        // message. Pausing pre-enqueue holds it until the user presses Resume.
        //
        // Already-Paused/Interrupted/Failed runs are skipped — the worker wouldn't start anyway.
        // Generating IS paused here too: keeping the running response and then queuing more
        // would surprise the user ("I queued it, why did it run?").
        //
        // SelectedRun being null is also a "ready to start" case: the dispatcher lazily creates
        // a new run on Enqueue for a branch that doesn't have one yet, and that new run starts
        // as Idle — which means Ctrl+Enter on a branch that hasn't loaded any run snapshot yet
        // (the typical state right after SelectChat) would also run immediately. StopAsync
        // creates the run as Paused if it doesn't exist, which is exactly what we need.
        //
        // The branch to pause MUST match the branch the upcoming Enqueue targets — otherwise
        // the pause lands on the main chat run while enqueue creates a fresh Idle run on the
        // selected branch's root, and the worker happily starts on that. runBranchId was
        // computed above and is the same value the enqueue will use.
        //
        // Fork is intentionally skipped: a forked branch doesn't exist on the server yet
        // (its root will be created by the enqueue), so pausing the future messageId would
        // create an empty run for it and then enqueue would still proceed against Idle. Better
        // to let Fork run immediately — the user explicitly asked for a new conversation.
        if (request.Mode == ComposerSubmitMode.Queue
            && request.SelectedRun is null or { Status: ChatRunStatus.Idle or ChatRunStatus.Generating or ChatRunStatus.Completed })
        {
            try
            {
                var paused = await chatRuns.StopAsync(projectId, chat.Id, runBranchId, Guid.CreateVersion7(), cancellationToken);
                // We don't keep a reference to the paused snapshot — the enqueue below returns
                // a fresh one. This keeps the service stateless across submits.
                _ = paused;
            }
            catch (HttpRequestException)
            {
                // Pre-pause is best-effort: even if it fails, EnqueueAsync still puts the message
                // in the queue. Surfacing an error here would discard the typed message.
            }
            catch (InvalidOperationException)
            {
                // Same as above.
            }
        }

        // Step 6: enqueue. This is the only call that actually creates the message on the
        // server. Everything before it is setup; everything after it is cleanup.
        ChatRunSnapshot snapshot;
        try
        {
            snapshot = await chatRuns.EnqueueAsync(
                projectId,
                chat.Id,
                new EnqueueChatMessageRequest(Guid.CreateVersion7(), messageId, prompt, enqueueParentId, runBranchId),
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            return new ComposerSubmitOutcome.Rejected("The endpoint did not accept the request. Verify the base URL, model, and API key.");
        }
        catch (InvalidOperationException)
        {
            return new ComposerSubmitOutcome.Rejected("The endpoint did not accept the request. Verify the base URL, model, and API key.");
        }

        // Step 7: auto-resume for Send mode. A plain Enter on a paused/interrupted/failed run
        // would otherwise leave the message stuck in the queue (the worker only fires for
        // Idle/Generating/Completed). Resuming once on submit is the cleanest fix.
        if (request.Mode != ComposerSubmitMode.Queue
            && snapshot.Status is ChatRunStatus.Paused or ChatRunStatus.Interrupted or ChatRunStatus.Failed)
        {
            try
            {
                var resumed = await chatRuns.ResumeAsync(projectId, chat.Id, runBranchId, Guid.CreateVersion7(), cancellationToken);
                if (resumed is not null)
                {
                    snapshot = resumed;
                }
            }
            catch (HttpRequestException)
            {
                // Resume failure isn't fatal — the message is queued, the user can press Resume.
            }
            catch (InvalidOperationException)
            {
                // Same as above.
            }
        }

        // Compute heldInQueue AFTER resume so a successful auto-resume reports the run as no
        // longer held. Otherwise the UI would still hide the action buttons even though the
        // dispatcher is now happily processing the queued message.
        var heldInQueue = request.Mode == ComposerSubmitMode.Queue
            || snapshot.Status is ChatRunStatus.Paused or ChatRunStatus.Interrupted or ChatRunStatus.Failed;

        return new ComposerSubmitOutcome.Accepted(chat, snapshot, runBranchId, branchLeafId, heldInQueue);
    }

    private static string CreateChatTitle(string message)
    {
        var trimmed = message.Trim();
        return trimmed.Length == 0
            ? "New chat"
            : trimmed[..Math.Min(trimmed.Length, NewChatTitleMaxLength)];
    }
}

/// <summary>
/// Small helpers kept separate so the service file stays focused on orchestration and the helpers
/// can be tested without a service instance.
/// </summary>
internal static class BranchLeafHelpers
{
    /// <summary>
    /// Maps a branch leaf id to the run branch id used by the dispatcher. The dispatcher runs
    /// a chat's "main" branch (the one rooted at the chat id) and a separate run per user-forked
    /// branch. Forked leaves map to their own root; the main leaf falls back to the chat id so
    /// the default run picks it up.
    ///
    /// A fork root is a message that isn't the first (chronologically) child of its parent —
    /// same definition Home.razor's ComputeBranchTreeItems uses for BranchTreeItem roots. Walking
    /// up from the leaf, the FIRST such sibling encountered is the branch's anchor id (that's
    /// what ChatComposerService used as runBranchId when the fork was originally enqueued — see
    /// Step 4 above, `messageId` for a fresh fork). If we reach the chat's true root without ever
    /// finding one, the leaf is on the plain main branch and chat.Id is correct.
    /// </summary>
    public static Guid RunBranchIdFor(Guid? branchLeafId, ChatDetails chat)
    {
        if (branchLeafId is not { } leafId) return chat.Id;
        var byId = chat.Messages.ToDictionary(m => m.Id);
        var firstChildByParent = chat.Messages
            .GroupBy(m => m.ParentId ?? Guid.Empty)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.CreatedAt).First().Id);

        var current = leafId;
        while (byId.TryGetValue(current, out var message))
        {
            var parentKey = message.ParentId ?? Guid.Empty;
            if (firstChildByParent.TryGetValue(parentKey, out var firstChildId) && firstChildId != current)
            {
                return current;
            }
            if (message.ParentId is null) return chat.Id;
            current = message.ParentId.Value;
        }
        return chat.Id;
    }
}
