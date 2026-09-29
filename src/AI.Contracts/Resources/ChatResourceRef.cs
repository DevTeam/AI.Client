namespace AI.Contracts.Resources;

/// <summary>
/// A workspace object named by a chat turn. Its contents are never copied into the turn, except
/// <paramref name="Excerpt"/>: the Host fills it when the message is sent — the chosen lines of a
/// file, or the uncommitted changes of a repository — so the model sees what the user saw then.
/// </summary>
/// <param name="Path">
/// An absolute path for <see cref="ChatResourceKind.File"/>, <see cref="ChatResourceKind.Directory"/>
/// and <see cref="ChatResourceKind.Diff"/> (the repository), the skill id for a skill, and the chat
/// or project id for <see cref="ChatResourceKind.Chat"/> and <see cref="ChatResourceKind.Project"/>.
/// </param>
/// <param name="Lines">For a file only: the lines the message is about, 1-based and inclusive.</param>
/// <param name="Mention">
/// The "@" link in the message text that names this reference, such as "@src/app.cs:12-40", or
/// null for one attached beside the text. The transcript draws the link in place of that text.
/// </param>
public sealed record ChatResourceRef(Guid Id, ChatResourceKind Kind, string Path, string? Name = null,
    ChatReviewKind? ReviewKind = null, ChatLineRange? Lines = null, string? Excerpt = null, string? Mention = null);

/// <summary>New kinds go at the end: the numbers are stored in chat history.</summary>
public enum ChatResourceKind { File, Directory, Review, Skill, Chat, Project, Diff }

/// <summary>Lines of a file, 1-based and inclusive.</summary>
public sealed record ChatLineRange(int Start, int End);
