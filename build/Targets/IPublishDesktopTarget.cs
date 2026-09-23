namespace Build.Targets;

internal interface IPublishDesktopTarget
{
    Task<int> RunAsync(string runtime, string outputDirectory, CancellationToken cancellationToken);
}
