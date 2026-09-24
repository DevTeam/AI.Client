namespace AI.Client.Contracts.Resources;

/// <summary>A workspace object named by a chat turn; its contents are never copied into the turn.</summary>
public sealed record ChatResourceRef(Guid Id, ChatResourceKind Kind, string Path);

public enum ChatResourceKind { File, Directory }
