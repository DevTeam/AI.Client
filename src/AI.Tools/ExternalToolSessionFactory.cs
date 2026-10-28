namespace AI.Infrastructure.Tools;

using Application.Tools;
using Contracts.Settings;
using Contracts.Tools;

/// <summary>
/// Opens a private session on a user-configured server, started for it and stopped with it. Used by
/// discovery, which may try a configuration that is not saved yet; chat runs go through the
/// pool in <see cref="CompositeToolSessionFactory"/> instead.
/// </summary>
public sealed class ExternalToolSessionFactory(
    IExternalMcpConnector connector,
    IToolResultModelProjector modelProjector) : IExternalToolSessionFactory
{
    public async Task<IToolSession> OpenAsync(
        McpServerSettings server, CancellationToken cancellationToken, string? credential = null)
    {
        var connection = await connector.ConnectAsync(server, credential, cancellationToken);
        return new McpToolSession(connection.Client, connection.Tools,
            server.Id, McpToolSession.ExternalPrefix(server.Id), canonicalizePaths: false, modelProjector);
    }
}
