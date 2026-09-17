namespace AI.Client.Infrastructure.Tools;

using AI.Client.Mcp.App;
using Application.Tools;
using Contracts.Settings;
using Contracts.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.IO.Pipelines;

/// <summary>
/// Connects to the application's own MCP server. The server runs inside this process, so the two
/// ends are joined by a pair of in-memory pipes instead of a child process's standard streams —
/// the same protocol, the same client, one less process.
/// </summary>
public sealed class AppToolSessionFactory(IAppMcpServerHost host) : IMcpServerConnection
{
    public Guid ServerId => AppMcpServer.Id;

    /// <summary>
    /// Directory grants mean nothing here: this server reaches application data, not the file
    /// system. The run does: a tool that asks the person a question has to know which conversation
    /// the question belongs to, and being built per session is what lets it be told rather than
    /// having to trust the model to name it.
    /// </summary>
    public async Task<IToolSession> OpenAsync(IReadOnlyList<ToolDirectoryGrant> directoryGrants, ToolRunContext run, CancellationToken cancellationToken)
    {
        // Two simplex pipes make one duplex link: the client writes where the server reads, and
        // reads where the server writes.
        var toServer = new Pipe();
        var toClient = new Pipe();
        var server = host.Create(new StreamServerTransport(
            toServer.Reader.AsStream(), toClient.Writer.AsStream(), AppMcpServerHost.Name), run);
        // The server's message loop lives as long as the session; it ends when the transport is
        // closed by disposing the server, which the session does on its way out.
        var serving = server.RunAsync(CancellationToken.None);
        var client = await McpClient.CreateAsync(
            new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()),
            cancellationToken: cancellationToken);
        try
        {
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            return new McpToolSession(client, tools.Select(tool => tool.ProtocolTool).ToArray(),
                AppMcpServer.Id, ToolRef.AppPrefix, canonicalizePaths: false,
                shutdown: async () =>
                {
                    await server.DisposeAsync();
                    // The loop is expected to end by cancellation of its own transport; a fault
                    // here would otherwise be lost on an unobserved task.
                    try { await serving; }
                    catch (OperationCanceledException) { }
                });
        }
        catch
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
            throw;
        }
    }
}
