namespace AI.Domain.Chats;

/// <summary>
/// The teammate a branch belongs to in team work: a short name unique in the team, its role, and
/// the colour every place that shows it draws it in. One identity, so the branch title, the message
/// headers and the transcript name the teammate the same way; see docs/34-asides-and-team-messages.md.
/// </summary>
public sealed record ChatBranchMember(string Name, string Role, string Color)
{
    /// <summary>
    /// The accent swatches a teammate is given, in the order they are handed out. Blue, the default
    /// accent, comes last so the first teammates stand apart from the application's own accent.
    /// </summary>
    public static readonly IReadOnlyList<string> Colors = ["teal", "amber", "purple", "green", "pink", "orange", "indigo", "blue"];

    public string Label => $"{Name} · {Role}";
}
