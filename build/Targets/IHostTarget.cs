namespace Build.Targets;

internal interface IHostTarget
{
    Task<int> RunAsync(CancellationToken cancellationToken);
}
