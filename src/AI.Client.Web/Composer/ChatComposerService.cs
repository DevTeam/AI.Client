namespace AI.Client.Web.Composer;

using Contracts.Chats;
using Contracts.Runs;
using Chats;
using Runs;

public sealed class ChatComposerService(IChatHistoryApi chatHistory, IChatRunsApi chatRuns) : IChatComposerService
{
    public async Task<ComposerSubmitOutcome> SubmitAsync(ComposerSubmitRequest request, CancellationToken cancellationToken)
    {
        if (request.ProjectId is not { } projectId) return new ComposerSubmitOutcome.Rejected("Select a project.");
        if (string.IsNullOrWhiteSpace(request.Message)) return new ComposerSubmitOutcome.Rejected("The composer is empty.");
        // Send-now is itself a way to resolve a stuck run — it drops the command that is stuck —
        // so it is the one mode this gate must not block.
        if (request.SelectedRun is { Status: ChatRunStatus.Failed, CanRetry: false } && request.Mode != ComposerSubmitMode.SendNow)
            return new ComposerSubmitOutcome.Rejected("Resolve or skip the failed queued message before sending another one.");
        try
        {
            var prompt = request.Message.Trim();
            var chat = request.SelectedChat ?? await chatHistory.CreateAsync(projectId,
                new CreateChatRequest(prompt[..Math.Min(48, prompt.Length)], request.CredentialProfileId), cancellationToken);
            var mode = request.ReplaceSourceId is not null ? ChatSubmitMode.Replace
                : request.ForkSourceId is not null || request.Mode == ComposerSubmitMode.Fork ? ChatSubmitMode.Fork
                : request.Mode == ComposerSubmitMode.Queue ? ChatSubmitMode.Queue
                : request.Mode == ComposerSubmitMode.SendNow ? ChatSubmitMode.SendNow : ChatSubmitMode.Send;
            var parent = mode == ChatSubmitMode.Fork ? request.ForkSourceId ?? request.BranchLeafId : null;
            var sourceBranchId = request.SelectedBranchId ?? request.SelectedRun?.BranchId
                ?? BranchLeafHelpers.RunBranchIdFor(request.BranchLeafId, chat);
            var branchRevision = chat.Branches?.SingleOrDefault(branch => branch.Id == sourceBranchId)?.Revision;
            var snapshot = await chatRuns.SubmitAsync(projectId, chat.Id,
                new SubmitChatMessageRequest(Guid.CreateVersion7(), Guid.CreateVersion7(), prompt, mode,
                    BranchId: sourceBranchId,
                    ParentMode: mode == ChatSubmitMode.Fork ? MessageParentMode.Explicit : MessageParentMode.BranchHead,
                    ParentMessageId: parent,
                    ReplaceSourceId: request.ReplaceSourceId,
                    ExpectedBranchRevision: mode is ChatSubmitMode.Replace or ChatSubmitMode.Fork ? branchRevision : null,
                    HoldInQueue: request.Mode == ComposerSubmitMode.Queue), cancellationToken);
            return new ComposerSubmitOutcome.Accepted(chat, snapshot, snapshot.BranchId,
                parent ?? request.BranchLeafId, snapshot.Status == ChatRunStatus.Paused);
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException)
        {
            return new ComposerSubmitOutcome.Rejected(error.Message);
        }
    }
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
