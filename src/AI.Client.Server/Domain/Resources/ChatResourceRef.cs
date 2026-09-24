namespace AI.Client.Domain.Resources;

public sealed record ChatResourceRef(Guid Id, ChatResourceKind Kind, string Path);

public enum ChatResourceKind { File, Directory }
