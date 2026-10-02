namespace AI.Server.Hosting;

using Contracts.Runs;

public sealed class RunSnapshotComparer : IRunSnapshotComparer
{
    public bool IsStreamingAppend(ChatRunSnapshot old, ChatRunSnapshot current) =>
        current.Revision >= old.Revision
        && current.StreamingContent.Length > old.StreamingContent.Length
        && current.StreamingContent.StartsWith(old.StreamingContent, StringComparison.Ordinal)
        && current.DraftContent == old.DraftContent
        && SameApartFromText(old, current);

    public bool IsDraftAppend(ChatRunSnapshot old, ChatRunSnapshot current)
    {
        var previous = old.DraftContent ?? string.Empty;
        return current.Revision == old.Revision
            && current.DraftContent is { } draft
            && draft.Length > previous.Length
            && draft.StartsWith(previous, StringComparison.Ordinal)
            && current.StreamingContent == old.StreamingContent
            && SameApartFromText(old, current);
    }

    private static bool SameApartFromText(ChatRunSnapshot old, ChatRunSnapshot current) =>
        current.IsGuide == old.IsGuide && current.Status == old.Status
        && current.Queue.Count == old.Queue.Count
        && current.Queue.Zip(old.Queue).All(pair => QueueItemEqual(pair.First, pair.Second))
        && current.HasUnreadResponse == old.HasUnreadResponse
        && current.Error == old.Error
        && current.ChatRevision == old.ChatRevision
        && current.HeadMessageId == old.HeadMessageId
        && Equals(current.PendingApproval, old.PendingApproval)
        && Equals(current.PendingPrompt, old.PendingPrompt)
        && (current.ActiveTools ?? []).SequenceEqual(old.ActiveTools ?? [])
        && current.FailureCode == old.FailureCode
        && current.CanRetry == old.CanRetry
        && current.BranchRevision == old.BranchRevision
        && (current.RecoveryActions ?? []).SequenceEqual(old.RecoveryActions ?? [])
        && current.ActiveMessageId == old.ActiveMessageId
        && current.DraftToolCall == old.DraftToolCall
        && Equals(current.Wait, old.Wait)
        && Equals(current.Context, old.Context)
        && Equals(current.WorkspaceChanges, old.WorkspaceChanges)
        && ReferenceEquals(current.TurnUsage, old.TurnUsage);

    private static bool QueueItemEqual(QueuedChatMessage first, QueuedChatMessage second) =>
        first with { Resources = null } == second with { Resources = null }
        && (first.Resources ?? []).SequenceEqual(second.Resources ?? []);
}
