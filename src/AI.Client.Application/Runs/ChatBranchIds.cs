namespace AI.Client.Application.Runs;

using Contracts.Chats;

public static class ChatBranchIds
{
    public static IReadOnlySet<Guid> Get(ChatDetails chat) =>
        (chat.Branches ?? []).Select(branch => branch.Id).ToHashSet();
}
