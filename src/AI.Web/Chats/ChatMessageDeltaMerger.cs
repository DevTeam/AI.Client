namespace AI.Web.Chats;

using AI.Contracts.Chats;
using AI.Contracts.Runs;

public sealed class ChatMessageDeltaMerger : IChatMessageDeltaMerger
{
    public bool TryApply(ChatDetails current, ChatRunSnapshot run, out ChatDetails updated)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(run);
        updated = current;
        if (run.ChatId != current.Id || run.ChatRevision <= current.Revision || run.MessageDelta is not { } delta)
            return false;
        var currentBranches = current.Branches;
        if (currentBranches is null) return false;
        var branch = currentBranches.FirstOrDefault(branch => branch.Id == run.BranchId);
        if (branch is null) return false;

        var messages = current.Messages.ToList();
        var revision = current.Revision;
        var head = branch.HeadMessageId;
        foreach (var append in delta.Appends.OrderBy(item => item.Revision))
        {
            if (append.Revision <= revision) continue;
            if (append.BaseRevision != revision || append.Revision <= append.BaseRevision) return false;
            if (messages.Any(item => item.Id == append.Message.Id)) return false;
            // A replacement can share a parent with an old message while pruning that old path.
            // An append-only delta cannot describe the removal, so reload the transcript instead.
            if (append.Message.ParentId != head) return false;
            messages.Add(append.Message);
            head = append.Message.Id;
            revision = append.Revision;
        }

        // A mutation not represented by this bounded tail happened after its last append.
        if (revision != run.ChatRevision) return false;

        var branches = currentBranches.Select(branch => branch.Id == run.BranchId
            ? branch with { HeadMessageId = run.HeadMessageId, Revision = run.BranchRevision }
            : branch).ToArray();
        var lastUpdated = delta.Appends
            .Where(item => item.Revision <= revision)
            .Select(item => item.Message.CreatedAt)
            .DefaultIfEmpty(current.UpdatedAt)
            .Max();
        updated = current with
        {
            Revision = revision,
            UpdatedAt = lastUpdated > current.UpdatedAt ? lastUpdated : current.UpdatedAt,
            Messages = messages,
            Branches = branches
        };
        return true;
    }
}
