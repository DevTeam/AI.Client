namespace AI.Server.Hosting;

using Contracts.Runs;

public sealed class RunSnapshotComparer : IRunSnapshotComparer
{
    public bool IsStreamingAppend(ChatRunSnapshot old, ChatRunSnapshot current) =>
        current.Revision >= old.Revision
        && current.Status == old.Status
        && current.StreamingContent.Length > old.StreamingContent.Length
        && current.StreamingContent.StartsWith(old.StreamingContent, StringComparison.Ordinal)
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
        && current.DraftContent == old.DraftContent
        && Equals(current.WorkspaceChanges, old.WorkspaceChanges);

    private static bool QueueItemEqual(QueuedChatMessage first, QueuedChatMessage second) =>
        first with { Resources = null } == second with { Resources = null }
        && (first.Resources ?? []).SequenceEqual(second.Resources ?? []);
}
