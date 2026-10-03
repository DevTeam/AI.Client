namespace AI.Application.Tools;

using AI.Contracts.Settings;

public interface IExternalToolSessionFactory
{
    Task<IToolSession> OpenAsync(McpServerSettings server, CancellationToken cancellationToken,
        string? credential = null);
}
