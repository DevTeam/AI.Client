namespace Build.Targets;

internal interface IPackageReleaseTarget
{
    /// <param name="localAi">
    /// Also build Windows installers that carry the Local AI component. The regular installers, which
    /// the updater downloads, never carry it, so an update stays small and keeps an installed model.
    /// </param>
    Task<int> RunAsync(string runtime, string version, bool localAi, CancellationToken cancellationToken);
}
