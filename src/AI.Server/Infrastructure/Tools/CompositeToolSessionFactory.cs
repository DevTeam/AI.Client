namespace AI.Infrastructure.Tools;

using Application.Tools;
using Application.Settings;
using Contracts.Tools;

/// <summary>
/// Presents every MCP server the Host connects to as one set of tools. Each server keeps its own
/// identity and its own policies; this only decides which sessions to open and routes each call to
/// the session the tool came from.
/// </summary>
public sealed class CompositeToolSessionFactory(
    IEnumerable<IMcpServerConnection> connections,
    IGlobalSettingsRepository settings,
    ExternalToolSessionFactory external) : IToolSessionFactory
{
    public async Task<IToolSession> OpenAsync(
        IReadOnlyList<ToolDirectoryGrant> directoryGrants,
        IReadOnlySet<Guid> servers,
        ToolRunContext run,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(servers);
        var sessions = new List<IToolSession>();
        try
        {
            foreach (var connection in connections)
                if (servers.Contains(connection.ServerId))
                    sessions.Add(await connection.OpenAsync(directoryGrants, run, cancellationToken));
            var hostIds = connections.Select(connection => connection.ServerId).ToHashSet();
            var externalIds = servers.Where(id => !hostIds.Contains(id)).ToArray();
            if (externalIds.Length > 0)
            {
                var global = await settings.LoadAsync(cancellationToken);
                foreach (var id in externalIds)
                {
                    var server = global.McpServers.SingleOrDefault(item => item.Id == id)
                        ?? throw new InvalidOperationException("MCP server not found.");
                    sessions.Add(await external.OpenAsync(server, cancellationToken));
                }
            }
        }
        catch
        {
            // One server failing to start must not leave the others running with nobody holding them.
            foreach (var session in sessions) await session.DisposeAsync();
            throw;
        }

        return new Composite(sessions);
    }

    private sealed class Composite(IReadOnlyList<IToolSession> sessions) : IToolSession
    {
        private readonly Dictionary<string, IToolSession> _owners = sessions
            .SelectMany(session => session.Tools.Select(tool => (tool.ModelDefinition.Name, session)))
            .ToDictionary(item => item.Name, item => item.session, StringComparer.Ordinal);

        public IReadOnlyList<AgentTool> Tools { get; } = sessions.SelectMany(session => session.Tools).ToArray();

        public string ValidateArguments(AgentTool tool, string arguments) => Owner(tool).ValidateArguments(tool, arguments);

        public Task<ToolCallResult> CallAsync(AgentTool tool, string arguments, IProgress<ToolProgress>? progress,
            CancellationToken cancellationToken) => Owner(tool).CallAsync(tool, arguments, progress, cancellationToken);

        private IToolSession Owner(AgentTool tool)
        {
            ArgumentNullException.ThrowIfNull(tool);
            return _owners.TryGetValue(tool.ModelDefinition.Name, out var session)
                ? session
                : throw new ArgumentException("Unknown tool.", nameof(tool));
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var session in sessions) await session.DisposeAsync();
        }
    }
}
