namespace AI.Infrastructure.Tools;

using Application.Tools;
using Application.Settings;
using Application.Resources;
using Contracts.Settings;
using Contracts.Tools;
using Contracts.Resources;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using System.Text.Json;

/// <summary>
/// Presents every MCP server the Host connects to as one set of tools. Each server keeps its own
/// identity and its own policies; this only decides which sessions to open and routes each call to
/// the session the tool came from.
/// </summary>
/// <remarks>
/// A turn waits for its tools before its first step, so the servers are opened together rather
/// than one after another, and a server that fails is left out and reported instead of failing the
/// turn. A user-configured server whose tool list is known from an earlier start is not started
/// at all until one of its tools is called; the ones that are started come from a pool that keeps
/// them running between turns.
/// </remarks>
public sealed class CompositeToolSessionFactory(
    IEnumerable<IMcpServerConnection> connections,
    IGlobalSettingsRepository settings,
    IExternalMcpConnector connector,
    IExternalMcpServerPool pool,
    IMcpToolListCache toolLists,
    IResourceAssetService images,
    IToolResultModelProjector projector,
    ILogger<CompositeToolSessionFactory> logger) : IToolSessionFactory
{
    private static readonly Action<ILogger, string, string, Exception?> ServerUnavailable =
        LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(1, "McpServerUnavailable"),
            "The MCP server {Server} was left out of the tool session: {Reason}");

    public async Task<IToolSession> OpenAsync(
        IReadOnlyList<ToolDirectoryGrant> directoryGrants,
        IReadOnlySet<Guid> servers,
        ToolRunContext run,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(servers);
        var hostIds = connections.Select(connection => connection.ServerId).ToHashSet();
        var externalIds = servers.Where(id => !hostIds.Contains(id)).ToArray();
        IReadOnlyList<McpServerSettings> external = externalIds.Length > 0
            ? (await settings.LoadAsync(cancellationToken)).McpServers : [];
        var openings = connections.Where(connection => servers.Contains(connection.ServerId))
            .Select(connection => OpenOneAsync(connection.ServerId, HostServerName(connection.ServerId),
                () => connection.OpenAsync(directoryGrants, run, cancellationToken), cancellationToken))
            .Concat(externalIds.Select(id => external.SingleOrDefault(item => item.Id == id) is { } server
                ? OpenOneAsync(id, server.Name, () => OpenExternalAsync(server, cancellationToken), cancellationToken)
                : Task.FromResult(new Outcome(null, new ToolServerFailure(id, id.ToString(), "MCP server not found.")))))
            .ToArray();
        var outcomes = await Task.WhenAll(openings);
        var sessions = outcomes.Select(outcome => outcome.Session).OfType<IToolSession>().ToArray();
        if (cancellationToken.IsCancellationRequested)
        {
            // The others are not left running with nobody holding them.
            foreach (var session in sessions) await session.DisposeAsync();
            cancellationToken.ThrowIfCancellationRequested();
        }

        return new Composite(sessions, outcomes.Select(outcome => outcome.Failure).OfType<ToolServerFailure>().ToArray(),
            run.ProjectId, images, projector);
    }

    private static string HostServerName(Guid id) =>
        id == DefaultMcpServer.Id ? DefaultMcpServer.Settings.Name
        : id == AppMcpServer.Id ? AppMcpServer.Settings.Name
        : id.ToString();

    private async Task<Outcome> OpenOneAsync(Guid serverId, string name, Func<Task<IToolSession>> open,
        CancellationToken cancellationToken)
    {
        try
        {
            return new Outcome(await open(), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new Outcome(null, null);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            ServerUnavailable(logger, name, error.Message, error);
            return new Outcome(null, new ToolServerFailure(serverId, name, error.Message));
        }
    }

    private async Task<IToolSession> OpenExternalAsync(McpServerSettings server, CancellationToken cancellationToken)
    {
        var fingerprint = await connector.FingerprintAsync(server, cancellationToken);
        var prefix = McpToolSession.ExternalPrefix(server.Id);
        if (await CachedAsync(server.Id, fingerprint, cancellationToken) is { } cached)
        {
            var lazy = new LazyPooledServer(pool, server, fingerprint,
                live => StoreAsync(server.Id, fingerprint, live, cached.Raw));
            try
            {
                return new McpToolSession(lazy.ConnectAsync, lazy.CloseAsync, cached.Tools, server.Id, prefix,
                    canonicalizePaths: false, projector);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // A stored schema that no longer builds; starting the server lists the tools afresh.
            }
        }

        // Never listed under this configuration: the tools are not known until the server runs.
        var lease = await pool.LeaseAsync(server, fingerprint, cancellationToken);
        try
        {
            await StoreAsync(server.Id, fingerprint, lease.Tools, null);
            return new McpToolSession((_, _) => ValueTask.FromResult(lease.Client), lease.DisposeAsync, lease.Tools,
                server.Id, prefix, canonicalizePaths: false, projector);
        }
        catch
        {
            await lease.DisposeAsync();
            throw;
        }
    }

    /// <summary>The stored list as it was stored and as tools, or null when there is none to trust.</summary>
    private async Task<(IReadOnlyList<JsonElement> Raw, IReadOnlyList<Tool> Tools)?> CachedAsync(
        Guid serverId, string fingerprint, CancellationToken cancellationToken)
    {
        try
        {
            if (await toolLists.GetAsync(serverId, fingerprint, cancellationToken) is not { } raw) return null;
            var tools = raw.Select(item => item.Deserialize<Tool>(McpJsonUtilities.DefaultOptions)
                ?? throw new JsonException("Empty tool descriptor.")).ToArray();
            return (raw, tools);
        }
        catch (Exception error) when (error is not (OperationCanceledException or OutOfMemoryException))
        {
            return null;
        }
    }

    private async Task StoreAsync(Guid serverId, string fingerprint, IReadOnlyList<Tool> tools, IReadOnlyList<JsonElement>? previous)
    {
        var raw = tools.Select(tool => JsonSerializer.SerializeToElement(tool, McpJsonUtilities.DefaultOptions)).ToArray();
        if (previous is not null && previous.Select(item => item.GetRawText()).SequenceEqual(raw.Select(item => item.GetRawText())))
            return;
        try
        {
            await toolLists.SetAsync(serverId, fingerprint, raw, CancellationToken.None);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Not stored: the next turn starts the server to list its tools, as it did before.
        }
    }

    private sealed record Outcome(IToolSession? Session, ToolServerFailure? Failure);

    private sealed class Composite(IReadOnlyList<IToolSession> sessions, IReadOnlyList<ToolServerFailure> failures,
        Guid projectId, IResourceAssetService images, IToolResultModelProjector projector) : IToolSession
    {
        private readonly Dictionary<string, IToolSession> _owners = sessions
            .SelectMany(session => session.Tools.Select(tool => (tool.ModelDefinition.Name, session)))
            .ToDictionary(item => item.Name, item => item.session, StringComparer.Ordinal);

        public IReadOnlyList<AgentTool> Tools { get; } = sessions.SelectMany(session => session.Tools).ToArray();

        public IReadOnlyList<ToolServerFailure> Failures => failures;

        public string ValidateArguments(AgentTool tool, string arguments) => Owner(tool).ValidateArguments(tool, arguments);

        public async Task<ToolCallResult> CallAsync(AgentTool tool, string arguments, IProgress<ToolProgress>? progress,
            CancellationToken cancellationToken)
        {
            var result = await Owner(tool).CallAsync(tool, arguments, progress, cancellationToken);
            if (result.Content.All(item => item.Data is null)) return result;
            var blocks = new List<ToolContent>(result.Content.Count);
            foreach (var block in result.Content)
            {
                if (block.Kind != ToolContentKind.Image || block.Data is null || projectId == Guid.Empty)
                {
                    blocks.Add(block with { Data = null });
                    continue;
                }
                var resource = await images.StoreAsync(projectId, block.Data,
                    "Tool image", ChatResourceSource.Tool, tool.ModelDefinition.Name, cancellationToken);
                blocks.Add(block with { AssetId = resource.AssetId, MimeType = resource.MediaType,
                    Name = block.Name ?? tool.ModelDefinition.Name, Data = null });
            }
            return result with { Content = blocks,
                ModelContent = projector.Project(blocks, result.StructuredContent, result.IsError) };
        }

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
