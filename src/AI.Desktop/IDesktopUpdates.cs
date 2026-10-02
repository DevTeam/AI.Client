namespace AI.Desktop;

using AI.Updates;

internal interface IDesktopUpdates : IAsyncDisposable
{
    IUpdateManager Manager { get; }
    event Action? ShutdownRequested;
    void Start();
}
