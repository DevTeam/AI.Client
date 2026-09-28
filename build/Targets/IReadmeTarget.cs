namespace Build.Targets;

internal interface IReadmeTarget
{
    Task<int> RunAsync(CancellationToken cancellationToken);
}
