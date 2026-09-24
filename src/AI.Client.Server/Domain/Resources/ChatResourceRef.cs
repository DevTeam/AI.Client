namespace AI.Client.Domain.Resources;

public sealed record ChatResourceRef(Guid Id, ChatResourceKind Kind, string Path, string? Name = null);

public enum ChatResourceKind { File, Directory, Review }
