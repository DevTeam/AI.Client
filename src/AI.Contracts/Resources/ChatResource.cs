namespace AI.Contracts.Resources;

/// <summary>
/// A resource named by a chat turn. Workspace paths and captured excerpts share this shape with
/// uploaded files. Their bytes live in the project asset store and are addressed by AssetId.
/// </summary>
/// <param name="Path">
/// An absolute path for workspace <see cref="ChatResourceKind.File"/> and <see cref="ChatResourceKind.Directory"/>
/// and <see cref="ChatResourceKind.Diff"/> (the repository), the skill id for a skill, the chat
/// or project id for <see cref="ChatResourceKind.Chat"/> and <see cref="ChatResourceKind.Project"/>,
/// or the original location for an uploaded file.
/// </param>
/// <param name="Lines">For a file only: the lines the message is about, 1-based and inclusive.</param>
/// <param name="Mention">
/// The "@" link in the message text that names this reference, such as "@src/app.cs:12-40", or
/// null for one attached beside the text. The transcript draws the link in place of that text.
/// </param>
public sealed record ChatResource(Guid Id, ChatResourceKind Kind, string Path, string? Name = null,
    ChatReviewKind? ReviewKind = null, ChatLineRange? Lines = null, string? Excerpt = null, string? Mention = null,
    ChatResourceSource Source = ChatResourceSource.Workspace, string? AssetId = null,
    string? MediaType = null, long? Size = null);

/// <summary>New kinds go at the end: the numbers are stored in chat history.</summary>
public enum ChatResourceKind { File, Directory, Review, Skill, Chat, Project, Diff, Image }

public enum ChatResourceSource { Workspace, Url, Clipboard, Tool, Application, Upload }

/// <summary>Lines of a file, 1-based and inclusive.</summary>
public sealed record ChatLineRange(int Start, int End);
