namespace Build.Targets;

internal interface IRunTarget
{
    Task<int> RunAsync(CancellationToken cancellationToken);
}
