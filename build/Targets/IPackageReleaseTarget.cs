namespace Build.Targets;

internal interface IPackageReleaseTarget
{
    Task<int> RunAsync(string runtime, string version, CancellationToken cancellationToken);
}
