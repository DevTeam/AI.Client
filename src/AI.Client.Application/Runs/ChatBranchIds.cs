using AI.Client.Contracts.Chats;

namespace AI.Client.Application.Runs;

public static class ChatBranchIds
{
    public static IReadOnlySet<Guid> Get(ChatDetails chat)
    {
        var branchIds = new HashSet<Guid> { chat.Id };
        foreach (var siblings in chat.Messages.GroupBy(message => message.ParentId))
        {
            foreach (var branchRoot in siblings.OrderBy(message => message.CreatedAt).Skip(1).Where(message => message.Role == "User"))
            {
                branchIds.Add(branchRoot.Id);
            }
        }
        return branchIds;
    }
}
