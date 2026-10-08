namespace AI.Mcp.App;

using AI.Contracts.Runs;

/// <summary>
/// A run snapshot as a tool result shows it. The snapshot is built for the open chat page: it
/// carries the message tail, the diffs and the full arguments of the tool in flight, which for a
/// run that is submitting a long message is that message again. A model reading it needs the
/// status, the queue and what is in flight, not hundreds of kilobytes it already has.
/// </summary>
internal static class RunSnapshotView
{
    private const int TextLimit = 500;
    private const int StreamingLimit = 2_000;

    public static ChatRunSnapshot Compact(ChatRunSnapshot snapshot) => snapshot with
    {
        StreamingContent = Clip(snapshot.StreamingContent, StreamingLimit),
        Queue = snapshot.Queue.Select(item => item with { Content = Clip(item.Content, TextLimit) }).ToArray(),
        ActiveTools = snapshot.ActiveTools?.Select(tool => tool with { Arguments = Clip(tool.Arguments, TextLimit) }).ToArray(),
        MessageDelta = null,
        DraftContent = null,
        WorkspaceChanges = null,
        TurnUsage = null
    };

    private static string Clip(string text, int limit) =>
        text.Length <= limit ? text : text[..limit] + $"… ({text.Length - limit} more characters)";
}
