namespace AI.Client.Host;

using Application.Runs;

internal sealed class ChatRunHostedService(IChatRunDispatcher dispatcher) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => dispatcher.WarmUpAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => dispatcher.ShutdownAsync(cancellationToken);
}
