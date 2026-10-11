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
        // Replacement resolves a stuck run by abandoning its old command.
        if (request.SelectedRun is { Status: ChatRunStatus.Failed, CanRetry: false } && mode != ChatSubmitMode.Replace)
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
                        ApprovalMode: request.ApprovalMode, ReasoningEffort: request.ReasoningEffort), cancellationToken);
            }
            var parent = mode == ChatSubmitMode.Fork ? request.ForkSourceId ?? request.BranchLeafId : null;
            var sourceBranchId = request.ForkSourceId is { } forkSourceId
                ? BranchLeafHelpers.RunBranchIdForForkSource(forkSourceId, chat)
                : request.SelectedBranchId ?? request.SelectedRun?.BranchId
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
            return new ComposerSubmitOutcome.Accepted(chat, snapshot, snapshot.BranchId,
                parent ?? request.BranchLeafId, snapshot.Status == ChatRunStatus.Paused);
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException)
        {
            return new ComposerSubmitOutcome.Rejected(error.Message);
        }
    }

    private static ChatSubmitMode ResolveMode(ComposerSubmitRequest request) =>
        request.ReplaceSourceId is not null ? ChatSubmitMode.Replace
        : request.ForkSourceId is not null || request.Mode == ComposerSubmitMode.Fork ? ChatSubmitMode.Fork
        : request.Mode == ComposerSubmitMode.Queue ? ChatSubmitMode.Queue : ChatSubmitMode.Send;
}

internal static class BranchLeafHelpers
{
    public static Guid RunBranchIdForForkSource(Guid sourceId, ChatDetails chat)
    {
        var messages = chat.Messages.ToDictionary(message => message.Id);
        var ancestors = new Dictionary<Guid, int>();
        for (Guid? id = sourceId; id is { } current && messages.TryGetValue(current, out var message)
            && !ancestors.ContainsKey(current); id = message.ParentId)
            ancestors.Add(current, ancestors.Count);

        return chat.Branches?
            .Where(branch => branch.RootMessageId is { } root && ancestors.ContainsKey(root)
                && IsAncestor(messages, branch.HeadMessageId, sourceId))
            .OrderBy(branch => ancestors[branch.RootMessageId!.Value])
            .Select(branch => branch.Id)
            .FirstOrDefault() is { } branchId && branchId != Guid.Empty ? branchId : chat.Id;
    }

    private static bool IsAncestor(Dictionary<Guid, ChatMessageView> messages, Guid? headId, Guid sourceId)
    {
        var visited = new HashSet<Guid>();
        for (var id = headId; id is { } current && visited.Add(current) && messages.TryGetValue(current, out var message); id = message.ParentId)
            if (current == sourceId) return true;
        return false;
    }

    public static Guid RunBranchIdFor(Guid? leafId, ChatDetails chat)
    {
        if (leafId is null) return chat.Id;
        return chat.Branches?.FirstOrDefault(branch => branch.HeadMessageId == leafId)?.Id
            ?? chat.Branches?.FirstOrDefault(branch => branch.RootMessageId == leafId)?.Id ?? chat.Id;
    }
}
