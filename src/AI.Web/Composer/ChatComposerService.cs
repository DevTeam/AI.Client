namespace AI.Web.Composer;

using Contracts.Chats;
using Contracts.Runs;
using Chats;
using Runs;

public sealed class ChatComposerService(IChatHistoryApi chatHistory, IChatRunsApi chatRuns) : IChatComposerService
{
    public async Task<ComposerSubmitOutcome> SubmitAsync(ComposerSubmitRequest request, CancellationToken cancellationToken)
    {
        if (request.ProjectId is not { } projectId) return new ComposerSubmitOutcome.Rejected("Select a project.");
        if (string.IsNullOrWhiteSpace(request.Message) && request.Resources is not { Count: > 0 })
            return new ComposerSubmitOutcome.Rejected("The composer is empty.");
        var mode = ResolveMode(request);
        // Send-now and replacement both resolve a stuck run by abandoning its old command; an
        // aside starts no command, so a stuck run does not stand in its way.
        if (request.SelectedRun is { Status: ChatRunStatus.Failed, CanRetry: false }
            && mode is not ChatSubmitMode.SendNow and not ChatSubmitMode.Replace and not ChatSubmitMode.Aside)
            return new ComposerSubmitOutcome.Rejected("Resolve or skip the failed queued message before sending another one.");
        try
        {
            var prompt = request.Message.Trim();
            var chat = request.SelectedChat;
            if (chat is null)
            {
                var firstResource = request.Resources is { Count: > 0 } resources ? resources[0] : null;
                var title = prompt.Length > 0 ? prompt[..Math.Min(48, prompt.Length)]
                    : firstResource?.Name ?? firstResource?.Path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                        .LastOrDefault() ?? "Workspace references";
                chat = await chatHistory.CreateAsync(projectId,
                    new CreateChatRequest(title, request.CredentialProfileId, AutoTitlePending: true,
                        ApprovalMode: request.ApprovalMode), cancellationToken);
            }
            var parent = mode == ChatSubmitMode.Fork ? request.ForkSourceId ?? request.BranchLeafId : null;
            var sourceBranchId = request.SelectedBranchId ?? request.SelectedRun?.BranchId
                ?? BranchLeafHelpers.RunBranchIdFor(request.BranchLeafId, chat);
            var branchRevision = chat.Branches?.SingleOrDefault(branch => branch.Id == sourceBranchId)?.Revision;
            var messageId = Guid.CreateVersion7();
            var snapshot = await chatRuns.SubmitAsync(projectId, chat.Id,
                new SubmitChatMessageRequest(Guid.CreateVersion7(), messageId, prompt, mode,
                    BranchId: sourceBranchId,
                    ParentMode: mode == ChatSubmitMode.Fork
                        ? parent is null ? MessageParentMode.Root : MessageParentMode.Explicit
                        : MessageParentMode.BranchHead,
                    ParentMessageId: parent,
                    ReplaceSourceId: request.ReplaceSourceId,
                    ExpectedBranchRevision: mode is ChatSubmitMode.Replace or ChatSubmitMode.Fork ? branchRevision : null,
                    Resources: request.Resources), cancellationToken);
            // An aside the idle branch took at once is its new head; one waiting for a running turn
            // arrives with that turn, like the turn's own messages.
            var leaf = mode == ChatSubmitMode.Aside && snapshot.Queue.All(item => item.Id != messageId)
                ? snapshot.HeadMessageId ?? request.BranchLeafId
                : parent ?? request.BranchLeafId;
            return new ComposerSubmitOutcome.Accepted(chat, snapshot, snapshot.BranchId,
                leaf, snapshot.Status == ChatRunStatus.Paused);
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException)
        {
            return new ComposerSubmitOutcome.Rejected(error.Message);
        }
    }

    private static ChatSubmitMode ResolveMode(ComposerSubmitRequest request) =>
        request.ReplaceSourceId is not null ? ChatSubmitMode.Replace
        : request.ForkSourceId is not null || request.Mode == ComposerSubmitMode.Fork ? ChatSubmitMode.Fork
        : request.Mode == ComposerSubmitMode.Queue ? ChatSubmitMode.Queue
        : request.Mode == ComposerSubmitMode.Aside ? ChatSubmitMode.Aside
        : request.Mode == ComposerSubmitMode.SendNow ? ChatSubmitMode.SendNow : ChatSubmitMode.Send;
}

internal static class BranchLeafHelpers
{
    public static Guid RunBranchIdFor(Guid? leafId, ChatDetails chat)
    {
        if (leafId is null) return chat.Id;
        return chat.Branches?.FirstOrDefault(branch => branch.HeadMessageId == leafId)?.Id
            ?? chat.Branches?.FirstOrDefault(branch => branch.RootMessageId == leafId)?.Id ?? chat.Id;
    }
}
