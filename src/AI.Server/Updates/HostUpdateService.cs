namespace AI.Updates;

using AI.Application.Runs;
using AI.Server.Hosting;
using Microsoft.Extensions.Hosting;

public sealed class HostUpdateService : BackgroundService, IHostUpdateService
{
    public IUpdateManager Manager { get; }
    public Action? Shutdown { get; set; }

    public HostUpdateService(ServerOptions options, IUpdateManagerFactory factory, IChatRunDispatcher runs)
    {
        Manager = factory.Create("Host", options.DataDirectory,
            token => { token.ThrowIfCancellationRequested(); return Task.FromResult(runs.TryEnterUpdateMaintenance()); },
            runs.LeaveUpdateMaintenance, () => Shutdown?.Invoke());
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Manager.RunAsync(stoppingToken);

    public override void Dispose()
    {
        base.Dispose();
        Manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
