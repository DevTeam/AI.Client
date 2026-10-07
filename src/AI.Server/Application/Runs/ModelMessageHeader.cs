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
        if (message.Sender is { } sender)
        {
            parts.Add(From(sender, chat));
            // Only team messages carry an intent. Lead and teammates exchange them as ordinary user
            // messages, which push the loaded team playbook out of the active-skill window after a
            // few rounds, so each one names the protocol to keep following.
            if (sender.Intent is { Length: > 0 } && sender.ChatId == chat.Id)
                parts.Add(sender.BranchId == chat.Id
                    ? "team message from the lead: keep to the team-contribute protocol"
                    : "team message to the lead: handle it with the team-coordinate protocol");
        }
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
        var branch = chat.Branches?.SingleOrDefault(item => item.Id == sender.BranchId);
        // A teammate is named by its identity, not by the branch title a person may have renamed;
        // in a team the main branch is the lead.
        var name = branch?.Member is { } member ? member.Label
            : sender.BranchId == chat.Id
                ? chat.Branches?.Any(item => item.Member is not null) == true ? "the lead" : "the main branch"
            : branch?.Title is { Length: > 0 } title ? $"branch \"{title}\"" : "a branch";
        return $"From {name} (branchId {sender.BranchId}){intent}";
    }
}
