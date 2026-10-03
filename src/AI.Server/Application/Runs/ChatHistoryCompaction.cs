namespace AI.Application.Runs;

using Chat;
using Chats;
using Projects;
using Settings;
using Usage;
using Contracts.Chats;
using Contracts.Settings;
using Contracts.Runs;
using Contracts.Usage;

/// <summary>
/// Compacting a branch's history on request, between turns: the earlier turns are summarized by
/// the chat's own model and kept as a history checkpoint, which the next request starts from.
/// </summary>
public interface IChatHistoryCompaction
{
    /// <summary>The outcome, or null when the project, chat or branch does not exist.</summary>
    Task<HistoryCompactionResponse?> CompactAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken);
}

public sealed class ChatHistoryCompaction(
    IChatService chats,
    IProjectService projects,
    IGlobalSettingsRepository settings,
    IConnectionChoice connectionChoice,
    IGlobalSecretStore secrets,
    IChatContextBuilder contextBuilder,
    IHistoryCheckpointService history,
    IContextSummaryWriter summaryWriter,
    IChatCompletionClient completion,
    ITokenUsageMeter usageMeter,
    IChatRunDispatcher runs,
    IClock clock,
    IIdGenerator ids,
    IAdaptiveContextPolicy contextPolicy) : IChatHistoryCompaction
{
    /// <summary>
    /// At most this many recent turns stay in full, and only while they fit a fifth of the window
    /// together. None may be left: between turns everything can be summarized, and the next
    /// question starts from the summary.
    /// </summary>
    private const int MaxTurnsToKeep = 2;

    public async Task<HistoryCompactionResponse?> CompactAsync(Guid projectId, Guid chatId, Guid branchId,
        CancellationToken cancellationToken)
    {
        if (await chats.GetAsync(projectId, chatId, cancellationToken) is not { } chat
            || chat.Branches?.SingleOrDefault(branch => branch.Id == branchId)?.HeadMessageId is not { } head
            || await projects.GetAsync(projectId, cancellationToken) is not { } project)
            return null;
        // A running turn built its request before the checkpoint could exist and would not see it;
        // summarizing under it would also race the messages it is adding.
        if ((await runs.GetSnapshotAsync(cancellationToken)).Any(run =>
                run.ChatId == chatId && run.BranchId == branchId && run.Status == ChatRunStatus.Generating))
            return new HistoryCompactionResponse(HistoryCompactionStatus.Busy,
                Error: "The branch is answering. Compact it once the turn has finished.");

        var global = await settings.LoadAsync(cancellationToken);
        if (connectionChoice.Choose(global.Connections, chat.ConnectionId, project.ConnectionId) is not { } connection)
            return new HistoryCompactionResponse(HistoryCompactionStatus.Failed, Error: "Choose an enabled connection for this chat.");

        var context = await history.ApplyAsync(projectId, chatId,
            await contextBuilder.BuildAsync(chat, head, cancellationToken), cancellationToken);
        var budget = contextPolicy.ResolveCompaction(connection);
        var coverable = history.Coverable(context, new HistoryKeepPolicy(
            budget.HistoryKeepTokens, 0, MaxTurnsToKeep));
        if (coverable.Count == 0)
            return new HistoryCompactionResponse(HistoryCompactionStatus.NothingToCompact,
                Error: "The history is already summarized up to the latest turn.");

        var request = new ChatCompletionRequest(connection.BaseUrl, connection.Model,
            await secrets.GetAsync("connection", connection.Id, cancellationToken), "Summarize", connection.Id);
        ContextSummary? summary;
        using (usageMeter.Begin(new TokenUsageScope(TokenUsagePurpose.Compaction, projectId, chatId, branchId)))
            summary = await summaryWriter.WriteAsync(coverable, budget.HistorySummaryTargetTokens, new Summarizer(completion, request),
                cancellationToken, connection);
        if (summary is null)
            return new HistoryCompactionResponse(HistoryCompactionStatus.Failed, Error: "The model did not return a summary.");

        var checkpoint = new HistoryCheckpoint(ids.Create(),
            coverable.Last(message => message.MessageId is not null).MessageId!.Value, summary.Text, coverable.Count,
            summary.SourceCharacters, connection.Model, clock.UtcNow, HistoryCheckpointOrigin.Manual);
        await history.AddAsync(projectId, chatId, checkpoint, cancellationToken);
        return new HistoryCompactionResponse(HistoryCompactionStatus.Compacted, checkpoint);
    }

    private sealed class Summarizer(IChatCompletionClient completion, ChatCompletionRequest request) : IContextSummarizer
    {
        public async Task<string> SummarizeAsync(string prompt, CancellationToken cancellationToken) =>
            (await completion.CompleteAsync(request with
            {
                Message = prompt,
                ContextMessages = [new ChatCompletionMessage("user", prompt)],
                Tools = []
            }, cancellationToken)).Content;
    }
}
