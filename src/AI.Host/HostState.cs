namespace AI.Host;

/// <summary>What the Host is doing right now.</summary>
/// <param name="Phase">The stage of its life.</param>
/// <param name="Address">Where it listens, once <see cref="HostPhase.Running"/>.</param>
/// <param name="Detail">Why it waits, while <see cref="HostPhase.WaitingForDataDirectory"/>.</param>
internal sealed record HostState(HostPhase Phase, Uri? Address = null, string? Detail = null)
{
    public static readonly HostState Starting = new(HostPhase.Starting);
}

internal enum HostPhase
{
    Starting,

    /// <summary>Another process, usually AI Client Desktop, still holds the data directory.</summary>
    WaitingForDataDirectory,

    Running
}
