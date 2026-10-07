namespace AI.Application.Runs;

using Contracts.Chats;

/// <summary>
/// Puts the line the model reads above a user message that did not come from the person as a new
/// turn: one taken by a running turn, an aside, or one another branch's run submitted. Stored
/// messages keep their text as written; only the request carries the header.
/// </summary>
public sealed class ModelMessageHeader : IModelMessageHeader
{
    public string Apply(ChatMessageView message, ChatDetails chat, string content)
    {
        if (message.Role != "User") return content;
        var parts = new List<string>();
        if (message.Sender is { } sender) parts.Add(From(sender, chat));
        switch (message.Delivery)
        {
            case MessageDelivery.InTurn:
                parts.Add("added while you were working: take it into account and continue the task");
                break;
            case MessageDelivery.Aside:
                parts.Add("aside: added without asking for a reply of its own");
                break;
        }
        if (parts.Count == 0) return content;
        var header = $"[{string.Join("; ", parts)}]";
        return content.Length == 0 ? header : header + "\n" + content;
    }

    private static string From(MessageSender sender, ChatDetails chat)
    {
        var intent = sender.Intent is { Length: > 0 } value ? $" ({value})" : string.Empty;
        if (sender.ChatId != chat.Id)
            return $"From chat {sender.ChatId}, branch {sender.BranchId}{intent}";
        var title = chat.Branches?.SingleOrDefault(branch => branch.Id == sender.BranchId)?.Title;
        var name = sender.BranchId == chat.Id ? "the main branch"
            : title is { Length: > 0 } ? $"branch \"{title}\"" : "a branch";
        return $"From {name} (branchId {sender.BranchId}){intent}";
    }
}
