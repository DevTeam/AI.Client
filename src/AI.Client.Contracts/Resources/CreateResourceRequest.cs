namespace AI.Client.Contracts.Resources;

public sealed record CreateResourceRequest(ChatResourceKind Kind, string Path);
