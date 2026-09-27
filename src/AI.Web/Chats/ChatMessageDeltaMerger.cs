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
        if (current.Branches?.Any(branch => branch.Id == run.BranchId) != true)
            return false;

        var messages = current.Messages.ToList();
        var revision = current.Revision;
        foreach (var append in delta.Appends.OrderBy(item => item.Revision))
        {
            if (append.Revision <= revision) continue;
            if (append.BaseRevision != revision || append.Revision <= append.BaseRevision) return false;
            if (messages.Any(item => item.Id == append.Message.Id)) return false;
            messages.Add(append.Message);
            revision = append.Revision;
        }

        // A mutation not represented by this bounded tail happened after its last append.
        if (revision != run.ChatRevision) return false;

        var branches = current.Branches.Select(branch => branch.Id == run.BranchId
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
