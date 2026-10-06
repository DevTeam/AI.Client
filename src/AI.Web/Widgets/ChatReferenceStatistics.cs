namespace AI.Web.Widgets;

using AI.Contracts.Chats;
using AI.Contracts.Resources;

/// <summary>
/// One reference the chat was given or received, added up over the messages that name it. The same
/// file named in three messages is one entry with three mentions: the person referred to one thing,
/// not three.
/// </summary>
/// <param name="Reference">
/// The first occurrence on the branch. The widget renders it through <c>IResourcePresenter</c>, so
/// a label and a tooltip read exactly as they do in the transcript.
/// </param>
/// <param name="Mentions">How many messages in scope name this same target.</param>
/// <param name="FirstMessageId">The earliest message in scope that names it; the transcript scrolls there.</param>
/// <param name="FirstAt">When that message was recorded.</param>
public sealed record ChatReferenceEntry(
    ChatResource Reference,
    int Mentions,
    Guid FirstMessageId,
    DateTimeOffset FirstAt);

/// <summary>
/// What the chat was given and received: the references carried by the messages of the visible
/// branch, in the chosen scope. Every entry comes from <see cref="ChatMessageView.Resources"/>,
/// which the transcript stores with the message that named it. Nothing is inferred from the message
/// text, so a path written in prose is not counted as a reference.
/// </summary>
/// <param name="References">Distinct targets, most mentioned first.</param>
/// <param name="MessagesWithReferences">How many messages in scope carry at least one reference.</param>
/// <param name="Turns">How many turns the scope covers; matches the turn count the other widgets report.</param>
/// <param name="TurnsWithReferences">Turns in which at least one message named a reference.</param>
/// <param name="IsRunning">True while the scope's last turn is still going; the widget says so far.</param>
public sealed record ChatReferenceStatistics(
    IReadOnlyList<ChatReferenceEntry> References,
    int MessagesWithReferences,
    int Turns,
    int TurnsWithReferences,
    bool IsRunning)
{
    public static ChatReferenceStatistics Empty { get; } = new([], 0, 0, 0, false);

    public int Count(ChatResourceKind kind) => References.Count(entry => entry.Reference.Kind == kind);
}

/// <summary>Builds the reference figures for the visible branch and the chosen scope.</summary>
public interface IChatReferenceStatisticsCalculator
{
    /// <param name="branch">The visible branch, root to leaf.</param>
    /// <param name="isRunning">True while the last turn is still going; the widget says so far.</param>
    /// <param name="scope">Whole chat or last turn.</param>
    ChatReferenceStatistics Calculate(IReadOnlyList<ChatMessageView> branch, bool isRunning, ChatWidgetScope scope);
}

public sealed class ChatReferenceStatisticsCalculator : IChatReferenceStatisticsCalculator
{
    public ChatReferenceStatistics Calculate(IReadOnlyList<ChatMessageView> branch, bool isRunning, ChatWidgetScope scope)
    {
        var turns = SplitTurns(branch);
        if (scope == ChatWidgetScope.LastTurn && turns.Count > 1) turns = turns[^1..];

        var byTarget = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        var messagesWithReferences = 0;
        var turnsWithReferences = 0;
        foreach (var turn in turns)
        {
            var turnNamedSomething = false;
            foreach (var message in turn)
            {
                if (message.Resources is not { Count: > 0 } resources) continue;
                var messageNamedSomething = false;
                foreach (var resource in resources)
                {
                    var key = Key(resource);
                    if (byTarget.TryGetValue(key, out var known)) known.Add(message);
                    else byTarget[key] = new Accumulator(resource, message);
                    messageNamedSomething = true;
                }
                if (!messageNamedSomething) continue;
                messagesWithReferences++;
                turnNamedSomething = true;
            }
            if (turnNamedSomething) turnsWithReferences++;
        }

        var references = byTarget.Values
            .OrderByDescending(entry => entry.Mentions)
            .ThenBy(entry => entry.Reference.Kind)
            .ThenBy(entry => entry.Reference.Path, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.ToEntry())
            .ToArray();

        return new ChatReferenceStatistics(references, messagesWithReferences, turns.Count, turnsWithReferences,
            isRunning && scope == ChatWidgetScope.LastTurn);
    }

    // Two references are the same target when a person would call them the same thing: the same path,
    // and for a file the same line range, since a message about lines 12-40 is about a different part
    // of the file than a message about the whole of it. Uploaded files are matched by their asset id,
    // which is what actually holds their bytes.
    private static string Key(ChatResource resource) => resource.Kind switch
    {
        ChatResourceKind.Image when resource.AssetId is { } assetId => $"{resource.Kind}:{assetId}",
        ChatResourceKind.File when resource.AssetId is { } assetId => $"{resource.Kind}:{assetId}",
        ChatResourceKind.File => $"{resource.Kind}:{resource.Path}:{resource.Lines?.Start}-{resource.Lines?.End}",
        ChatResourceKind.Review => $"{resource.Kind}:{resource.Path}:{resource.ReviewKind}",
        _ => $"{resource.Kind}:{resource.Path}"
    };

    // A turn starts at a person's message; whatever comes before the first one belongs to it.
    private static List<List<ChatMessageView>> SplitTurns(IReadOnlyList<ChatMessageView> branch)
    {
        var turns = new List<List<ChatMessageView>>();
        foreach (var message in branch)
        {
            if (message.Role == "User" || turns.Count == 0) turns.Add([]);
            turns[^1].Add(message);
        }
        return turns;
    }

    private sealed class Accumulator(ChatResource reference, ChatMessageView message)
    {
        public ChatResource Reference { get; } = reference;

        public int Mentions { get; private set; } = 1;

        private Guid _firstMessageId = message.Id;
        private DateTimeOffset _firstAt = message.CreatedAt;

        public void Add(ChatMessageView other)
        {
            Mentions++;
            if (other.CreatedAt >= _firstAt) return;
            _firstAt = other.CreatedAt;
            _firstMessageId = other.Id;
        }

        public ChatReferenceEntry ToEntry() => new(Reference, Mentions, _firstMessageId, _firstAt);
    }
}
