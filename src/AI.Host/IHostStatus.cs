namespace AI.Host;

/// <summary>
/// The Host's current <see cref="HostState"/>: the server runner reports it, the tray icon shows it.
/// </summary>
internal interface IHostStatus
{
    HostState Current { get; }

    /// <summary>Raised on the reporting thread, never the UI one.</summary>
    event Action<HostState>? Changed;

    void Report(HostState state);
}
