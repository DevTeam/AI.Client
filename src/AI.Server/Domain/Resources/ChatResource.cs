namespace AI.Domain.Resources;

public sealed record ChatResource(Guid Id, ChatResourceKind Kind, string Path, string? Name = null,
    ChatReviewKind? ReviewKind = null, ChatLineRange? Lines = null, string? Excerpt = null, string? Mention = null,
    ChatResourceSource Source = ChatResourceSource.Workspace, string? AssetId = null,
    string? MediaType = null, long? Size = null);

public enum ChatResourceKind { File, Directory, Review, Skill, Chat, Project, Diff, Image }
public enum ChatResourceSource { Workspace, Url, Clipboard, Tool, Application, Upload }
public enum ChatReviewKind { Diff, Message }
public sealed record ChatLineRange(int Start, int End);
