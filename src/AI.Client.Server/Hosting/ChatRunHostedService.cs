namespace AI.Client.Server.Hosting;

using Application.Runs;
using Microsoft.Extensions.Hosting;

internal sealed class ChatRunHostedService(IChatRunDispatcher dispatcher) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => dispatcher.WarmUpAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => dispatcher.ShutdownAsync(cancellationToken);
}
