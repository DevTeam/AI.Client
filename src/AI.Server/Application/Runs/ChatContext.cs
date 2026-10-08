namespace AI.Application.Runs;

using Chat;
using Contracts.Chats;
using Contracts.Tools;
using AI.Application.Resources;

public sealed class ChatContext(IToolResultCodec toolResultCodec, IResourceModelProjection resources,
    IModelMessageHeader headers) : IChatContextBuilder
{
    public IReadOnlyList<ChatCompletionMessage> Build(ChatDetails chat, Guid headId)
    {
        var byId = chat.Messages.ToDictionary(message => message.Id);
        var path = new List<ChatCompletionMessage>();
        var visited = new HashSet<Guid>();
        Guid? current = headId;
        while (current is { } id)
        {
            if (!visited.Add(id) || !byId.TryGetValue(id, out var message))
                throw new InvalidOperationException("Invalid message ancestry.");
            var role = message.Role.ToLowerInvariant();
            var modelContent = role == "tool" ? toolResultCodec.TryRead(message.Content)?.ModelContent : null;
            if (role == "user") modelContent = headers.Apply(message, chat, resources.Project(message.Content, message.Resources));
            path.Add(new ChatCompletionMessage(role, message.Content, message.ToolCalls, message.ToolCallId, modelContent, message.Id,
                ImageAssetIds: message.Resources?.Where(item => item.Kind == AI.Contracts.Resources.ChatResourceKind.Image)
                    .Select(item => item.AssetId).OfType<string>().ToArray(),
                JoinsTurn: message.Delivery == MessageDelivery.InTurn));
            current = message.ParentId;
        }
        path.Reverse();
        return WithToolImages(RepairToolExchanges(path));
    }

    public async Task<IReadOnlyList<ChatCompletionMessage>> BuildAsync(ChatDetails chat, Guid headId,
        CancellationToken cancellationToken)
    {
        var byId = chat.Messages.ToDictionary(message => message.Id);
        var path = new List<ChatCompletionMessage>();
        var visited = new HashSet<Guid>();
        Guid? current = headId;
        while (current is { } id)
        {
            if (!visited.Add(id) || !byId.TryGetValue(id, out var message))
                throw new InvalidOperationException("Invalid message ancestry.");
            var role = message.Role.ToLowerInvariant();
            var modelContent = role == "tool" ? toolResultCodec.TryRead(message.Content)?.ModelContent : null;
            if (role == "user") modelContent = headers.Apply(message, chat, await resources.ProjectAsync(chat.ProjectId,
                chat.Id, message.Content, message.Resources, cancellationToken));
            path.Add(new ChatCompletionMessage(role, message.Content, message.ToolCalls, message.ToolCallId, modelContent, message.Id,
                ImageAssetIds: message.Resources?.Where(item => item.Kind == AI.Contracts.Resources.ChatResourceKind.Image)
                    .Select(item => item.AssetId).OfType<string>().ToArray(),
                JoinsTurn: message.Delivery == MessageDelivery.InTurn));
            current = message.ParentId;
        }
        path.Reverse();
        return WithToolImages(RepairToolExchanges(path));
    }

    private List<ChatCompletionMessage> WithToolImages(List<ChatCompletionMessage> path)
    {
        var result = new List<ChatCompletionMessage>();
        var images = new List<string>();
        for (var index = 0; index < path.Count; index++)
        {
            var message = path[index];
            result.Add(message);
            if (message.Role != "tool") continue;
            images.AddRange(toolResultCodec.TryRead(message.Content)?.Content
                .Where(item => item.Kind == ToolContentKind.Image).Select(item => item.AssetId).OfType<string>() ?? []);
            if (images.Count == 0 || index + 1 < path.Count && path[index + 1].Role == "tool") continue;
            result.Add(new ChatCompletionMessage("user", "Images returned by the preceding tools:",
                IsContextSummary: true, ImageAssetIds: images.ToArray()));
            images.Clear();
        }
        return result;
    }

    private static List<ChatCompletionMessage> RepairToolExchanges(IReadOnlyList<ChatCompletionMessage> path)
    {
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
