namespace AI.Host;

internal sealed class HostStatus : IHostStatus
{
    private HostState _current = HostState.Starting;

    public HostState Current => Volatile.Read(ref _current);

    public event Action<HostState>? Changed;

    public void Report(HostState state)
    {
        Volatile.Write(ref _current, state);
        Changed?.Invoke(state);
    }
}
