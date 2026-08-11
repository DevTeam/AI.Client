namespace AI.Client.Contracts.Runs;

public sealed record UpdateQueuedMessageRequest(Guid OperationId, string? Content = null, int? Position = null);
