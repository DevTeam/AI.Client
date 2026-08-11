namespace Build.Targets;

internal interface IPublishTarget
{
    Task<int> RunAsync(string outputDirectory, CancellationToken cancellationToken);
}
