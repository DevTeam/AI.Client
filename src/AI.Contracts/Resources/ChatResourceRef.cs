namespace AI.Contracts.Resources;

/// <summary>A workspace object named by a chat turn; its contents are never copied into the turn.</summary>
public sealed record ChatResourceRef(Guid Id, ChatResourceKind Kind, string Path, string? Name = null,
    ChatReviewKind? ReviewKind = null);

public enum ChatResourceKind { File, Directory, Review, Skill }
