namespace AI.Contracts.Resources;

/// <summary>A mutable, named chat resource anchored to saved changes or a chat message.</summary>
public sealed record ChatReview(Guid Id, Guid ProjectId, Guid ChatId, string Name, Guid SourceMessageId,
    DateTimeOffset SourceCreatedAt, IReadOnlyList<string> Files, IReadOnlyList<ReviewComment> Comments,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Revision,
    ChatReviewKind Kind = ChatReviewKind.Diff,
    IReadOnlyList<MessageReviewComment>? MessageComments = null,
    Workspace.WorkspaceChangeSet? SourceChanges = null);

public enum ChatReviewKind { Diff, Message }

public sealed record MessageReviewComment(Guid Id, int Start, int End, string Quote, string Body);

/// <summary>Either a whole-file comment or a range in a saved unified diff.</summary>
public sealed record ReviewComment(Guid Id, string Path, int? OldStart, int? OldEnd,
    int? NewStart, int? NewEnd, string Body);

public sealed record CreateReviewRequest(Guid SourceMessageId, string Name, IReadOnlyList<string> Files,
    ChatReviewKind Kind = ChatReviewKind.Diff,
    IReadOnlyList<MessageReviewComment>? MessageComments = null,
    IReadOnlyList<ReviewComment>? Comments = null);
public sealed record UpdateReviewRequest(string Name, IReadOnlyList<string> Files,
    IReadOnlyList<ReviewComment> Comments, long ExpectedRevision,
    IReadOnlyList<MessageReviewComment>? MessageComments = null);
