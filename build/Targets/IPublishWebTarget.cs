namespace Build.Targets;

internal interface IPublishWebTarget
{
    Task<int> RunAsync(CancellationToken cancellationToken);
}
