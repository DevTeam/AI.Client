namespace AI.Domain.Resources;

public sealed record ChatResourceRef(Guid Id, ChatResourceKind Kind, string Path, string? Name = null,
    ChatReviewKind? ReviewKind = null);

public enum ChatResourceKind { File, Directory, Review, Skill }
public enum ChatReviewKind { Diff, Message }
