namespace AI.Client.Contracts.Runs;

/// <summary>
/// How far a queued command has got. The distinction is what the queue UI is built on: a
/// <see cref="Prepared"/> entry has not been sent anywhere and can still be edited, reordered or
/// removed, while a <see cref="UserCommitted"/> one is already a message in the transcript and
/// only exists in the queue so the run can be retried or resumed from it.
/// </summary>
public enum QueuedMessageStage { Prepared, UserCommitted }
