namespace AI.Contracts.Chats;

using Runs;

/// <summary>Where a teammate stands, as its run and its reports say.</summary>
public enum ChatTeamMemberState
{
    /// <summary>Its run is generating.</summary>
    Working,

    /// <summary>Its run waits for a person: a tool confirmation or a question.</summary>
    NeedsPerson,

    /// <summary>Its run failed, was interrupted or paused, and does not continue on its own.</summary>
    Stopped,

    /// <summary>Its latest report is <c>done</c> and nothing has been asked of it since.</summary>
    Done,

    /// <summary>Nothing running: it waits for an answer, a message or its next phase.</summary>
    Waiting
}

/// <summary>One team message: what it was meant to be and its first line.</summary>
/// <param name="At">When it was stored, which is when it reached the receiving branch.</param>
public sealed record ChatTeamReport(Guid MessageId, string? Intent, string Text, DateTimeOffset At);

/// <summary>One member of the team, the lead included.</summary>
/// <param name="Color">The accent swatch the teammate wears; null for the lead.</param>
/// <param name="IsCurrent">The visible branch is this member's.</param>
/// <param name="LastReport">The latest message this teammate sent to the lead.</param>
/// <param name="Reports">How many messages this teammate sent to the lead.</param>
/// <param name="Open">
/// A question or blocker of this teammate that the lead has not answered: nothing reached its
/// branch from the lead after it. For the lead, null; its open items are the teammates' ones.
/// </param>
public sealed record ChatTeamMember(
    Guid BranchId,
    string Name,
    string Role,
    TeamMember? Identity,
    bool IsLead,
    bool IsCurrent,
    ChatTeamMemberState State,
    ChatTeamReport? LastReport,
    int Reports,
    ChatTeamReport? Open);

/// <summary>
/// The whole team of a chat, the same whichever branch is open: the task as the person gave it, the
/// charter the lead wrote, and every member with its state, its latest report and what it waits for.
/// </summary>
/// <param name="Task">The person's first message on the main branch: the task the team works on.</param>
/// <param name="Charter">The lead's latest "Team charter" message on the main branch.</param>
/// <param name="Members">The lead first, then the teammates in the order they were started.</param>
public sealed record ChatTeamRoster(
    ChatTeamReport? Task,
    ChatTeamReport? Charter,
    IReadOnlyList<ChatTeamMember> Members)
{
    public static ChatTeamRoster Empty { get; } = new(null, null, []);

    /// <summary>A chat is a team once it has at least one teammate's branch.</summary>
    public bool IsTeam => Members.Any(member => !member.IsLead);

    public int Teammates => Members.Count(member => !member.IsLead);

    public int Done => Members.Count(member => member.State == ChatTeamMemberState.Done);

    public int Open => Members.Count(member => member.Open is not null);
}

/// <summary>
/// Builds the roster of a chat's team. Teammates are the branches that carry a
/// <see cref="ChatBranchView.Member"/> identity; reports are the messages whose
/// <see cref="ChatMessageView.Sender"/> the Host recorded, so nothing is inferred from text but the
/// charter's heading and the first line shown for each message.
/// </summary>
public interface IChatTeamRosterCalculator
{
    /// <param name="chat">The chat, with the messages of every branch.</param>
    /// <param name="runs">The chat's run snapshots, one per branch that has run.</param>
    /// <param name="selectedBranchId">The visible branch.</param>
    ChatTeamRoster Calculate(ChatDetails? chat, IReadOnlyList<ChatRunSnapshot> runs, Guid? selectedBranchId);
}

public sealed class ChatTeamRosterCalculator : IChatTeamRosterCalculator
{
    private const int MaxLine = 160;

    public ChatTeamRoster Calculate(ChatDetails? chat, IReadOnlyList<ChatRunSnapshot> runs, Guid? selectedBranchId)
    {
        if (chat?.Branches is not { } branches || !branches.Any(branch => branch.Member is not null))
            return ChatTeamRoster.Empty;
        var byId = chat.Messages.ToDictionary(message => message.Id);
        var current = selectedBranchId ?? chat.Id;
        var main = Chain(byId, branches.SingleOrDefault(branch => branch.Id == chat.Id)?.HeadMessageId);

        var task = main.FirstOrDefault(message => message is { Role: "User", Sender: null, Delivery: MessageDelivery.Turn });
        var charter = main.LastOrDefault(message => message.Role == "User" && FirstLine(message.Content)
            .StartsWith("Team charter", StringComparison.OrdinalIgnoreCase));
        var reports = chat.Messages
            .Where(message => message.Sender is { } sender && sender.ChatId == chat.Id && sender.BranchId != chat.Id)
            .GroupBy(message => message.Sender!.BranchId)
            .ToDictionary(group => group.Key, group => group.OrderBy(message => message.CreatedAt).ToArray());

        var members = new List<ChatTeamMember>
        {
            new(chat.Id, "Lead", "Coordinator", null, true, current == chat.Id, State(Run(runs, chat.Id), null), null,
                0, null)
        };
        foreach (var branch in branches.Where(branch => branch.Member is not null)
                     .OrderBy(branch => branch.RootMessageId is { } root && byId.TryGetValue(root, out var first)
                         ? first.CreatedAt : DateTimeOffset.MaxValue))
        {
            var sent = reports.GetValueOrDefault(branch.Id) ?? [];
            var last = sent.Length == 0 ? null : Report(sent[^1]);
            // What the lead last sent into this branch: an answer, a decision, or the brief itself.
            var fromLead = Chain(byId, branch.HeadMessageId)
                .Where(message => message.Sender is { } sender && sender.ChatId == chat.Id && sender.BranchId == chat.Id)
                .Select(message => message.CreatedAt)
                .DefaultIfEmpty(DateTimeOffset.MinValue)
                .Max();
            var asked = sent.LastOrDefault(message => message.Sender!.Intent is "question" or "blocker");
            var open = asked is not null && asked.CreatedAt > fromLead ? Report(asked) : null;
            members.Add(new ChatTeamMember(branch.Id, branch.Member!.Name, branch.Member.Role, branch.Member, false,
                current == branch.Id, State(Run(runs, branch.Id), last), last, sent.Length, open));
        }

        return new ChatTeamRoster(task is null ? null : Report(task), charter is null ? null : Report(charter), members);
    }

    private static ChatRunSnapshot? Run(IReadOnlyList<ChatRunSnapshot> runs, Guid branchId) =>
        runs.FirstOrDefault(run => run.BranchId == branchId);

    private static ChatTeamMemberState State(ChatRunSnapshot? run, ChatTeamReport? last) => run switch
    {
        { PendingApproval: not null } or { PendingPrompt: not null } => ChatTeamMemberState.NeedsPerson,
        { Status: ChatRunStatus.Generating } => ChatTeamMemberState.Working,
        { Status: ChatRunStatus.Failed or ChatRunStatus.Interrupted or ChatRunStatus.Paused } => ChatTeamMemberState.Stopped,
        _ => last?.Intent == "done" ? ChatTeamMemberState.Done : ChatTeamMemberState.Waiting
    };

    private static ChatTeamReport Report(ChatMessageView message) =>
        new(message.Id, message.Sender?.Intent is { Length: > 0 } intent ? intent.Trim().ToLowerInvariant() : null,
            FirstLine(message.Content), message.CreatedAt);

    // The first line that says something: headings lose their marks, and a long line is cut where
    // the widget would have cut it anyway.
    private static string FirstLine(string content)
    {
        foreach (var raw in content.Split('\n'))
        {
            // Shown as plain text, so inline code and emphasis marks would only be noise.
            var line = raw.Trim().TrimStart('#', '>', '*', '-', ' ').Replace("`", "", StringComparison.Ordinal)
                .Replace("**", "", StringComparison.Ordinal).Trim();
            if (line.Length == 0) continue;
            return line.Length <= MaxLine ? line : line[..(MaxLine - 1)] + "…";
        }
        return string.Empty;
    }

    private static List<ChatMessageView> Chain(Dictionary<Guid, ChatMessageView> byId, Guid? head)
    {
        var chain = new List<ChatMessageView>();
        var visited = new HashSet<Guid>();
        for (var cursor = head; cursor is { } id && visited.Add(id) && byId.TryGetValue(id, out var message);
             cursor = message.ParentId)
            chain.Add(message);
        chain.Reverse();
        return chain;
    }
}
