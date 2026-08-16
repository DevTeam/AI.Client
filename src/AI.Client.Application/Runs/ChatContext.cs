namespace AI.Client.Application.Runs;

using Contracts.Chat;
using Contracts.Chats;

public static class ChatContext
{
    public static IReadOnlyList<ChatCompletionMessage> Get(ChatDetails chat, Guid headId)
    {
        var byId = chat.Messages.ToDictionary(message => message.Id);
        var path = new List<ChatCompletionMessage>();
        var visited = new HashSet<Guid>();
        Guid? current = headId;
        while (current is { } id)
        {
            if (!visited.Add(id) || !byId.TryGetValue(id, out var message))
                throw new InvalidOperationException("Invalid message ancestry.");
            path.Add(new ChatCompletionMessage(message.Role.ToLowerInvariant(), message.Content));
            current = message.ParentId;
        }
        path.Reverse();
        return path;
    }
}
