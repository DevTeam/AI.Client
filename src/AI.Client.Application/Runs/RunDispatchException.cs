namespace AI.Client.Application.Runs;

using Domain.Runs;

internal sealed class RunDispatchException(RunFailureKind failureKind, string message) : Exception(message)
{
    public RunFailureKind FailureKind { get; } = failureKind;
}
