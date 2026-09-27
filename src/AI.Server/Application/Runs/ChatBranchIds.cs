namespace AI.Application.Runs;

using Contracts.Chats;

public sealed class ChatBranchIds : IChatBranchIds
{
    public IReadOnlySet<Guid> Collect(ChatDetails chat) =>
        (chat.Branches ?? []).Select(branch => branch.Id).ToHashSet();
}
