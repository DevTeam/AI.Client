namespace AI.Server.Hosting;

using Application.Projects;
using Application.Runs;
using Microsoft.Extensions.Hosting;

internal sealed class ChatRunHostedService(IChatRunDispatcher dispatcher, IGuideChats guideChats, IProjectService projects) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await dispatcher.WarmUpAsync(cancellationToken);
        // A guide tour cannot outlive the Host: the window that drove it is gone. Its service chat
        // is removed in the background so that startup does not wait for it.
        _ = Task.Run(() => RemoveGuideChatsAsync(CancellationToken.None), CancellationToken.None);
    }

    public Task StopAsync(CancellationToken cancellationToken) => dispatcher.ShutdownAsync(cancellationToken);

    private async Task RemoveGuideChatsAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var project in await projects.ListAsync(cancellationToken))
                await guideChats.CleanUpAsync(project.Id, null, cancellationToken);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { }
    }
}
