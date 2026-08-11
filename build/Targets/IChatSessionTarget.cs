namespace Build.Targets;

internal interface IChatSessionTarget
{
    Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}
