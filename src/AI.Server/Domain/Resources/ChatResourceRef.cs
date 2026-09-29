namespace AI.Domain.Resources;

public sealed record ChatResourceRef(Guid Id, ChatResourceKind Kind, string Path, string? Name = null,
    ChatReviewKind? ReviewKind = null, ChatLineRange? Lines = null, string? Excerpt = null, string? Mention = null);

public enum ChatResourceKind { File, Directory, Review, Skill, Chat, Project, Diff }
public enum ChatReviewKind { Diff, Message }
public sealed record ChatLineRange(int Start, int End);
