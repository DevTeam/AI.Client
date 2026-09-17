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
            path.Add(new ChatCompletionMessage(message.Role.ToLowerInvariant(), message.Content, message.ToolCalls, message.ToolCallId));
            current = message.ParentId;
        }
        path.Reverse();
        // A stopped run or a fork may end inside a tool exchange. Never execute missing results on recovery.
        var complete = new List<ChatCompletionMessage>();
        // Keep the model's call order. Some compatible endpoints enforce it even though calls are
        // matched by id, and HashSet enumeration made a repaired batch intermittently invalid.
        var pending = new List<string>();
        foreach (var message in path)
        {
            if (message.Role != "tool")
            {
                foreach (var id in pending) complete.Add(new ChatCompletionMessage("tool",
                    "{\"isError\":true,\"error\":\"Interrupted invocation; outcome unknown. Do not automatically repeat it.\"}", ToolCallId: id));
                pending.Clear();
            }
            if (message.ToolCallId is { } callId && !pending.Remove(callId)) continue;
            complete.Add(message);
            foreach (var call in message.ToolCalls ?? [])
                if (!pending.Contains(call.Id, StringComparer.Ordinal)) pending.Add(call.Id);
        }
        foreach (var id in pending) complete.Add(new ChatCompletionMessage("tool",
            "{\"isError\":true,\"error\":\"Interrupted invocation; outcome unknown. Do not automatically repeat it.\"}", ToolCallId: id));
        return complete;
    }
}
