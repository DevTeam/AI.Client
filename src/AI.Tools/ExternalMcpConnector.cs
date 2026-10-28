namespace AI.Infrastructure.Tools;

using AI.Contracts.FileSystem;
using Application.Settings;
using Contracts.Settings;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>A started external MCP server: its client and the tools it listed.</summary>
public sealed record ExternalMcpConnection(McpClient Client, IReadOnlyList<Tool> Tools) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Client.DisposeAsync();
}

/// <summary>Starts user-configured MCP servers and tells their configurations apart.</summary>
public interface IExternalMcpConnector
{
    /// <summary>How long a server may take to start and list its tools.</summary>
    TimeSpan StartTimeout { get; }

    /// <summary>
    /// Starts the server and lists its tools. A server that does not finish within
    /// <see cref="StartTimeout"/> fails with a <see cref="TimeoutException"/> naming it, never with
    /// a cancellation the caller did not ask for.
    /// </summary>
    Task<ExternalMcpConnection> ConnectAsync(McpServerSettings server, string? credential, CancellationToken cancellationToken);

    /// <summary>
    /// A hash of everything that decides which process is started and what it can reach: command,
    /// arguments, directory, environment and credential, saved secrets included. Two servers with the
    /// same fingerprint are interchangeable; a changed one is a different server.
    /// </summary>
    Task<string> FingerprintAsync(McpServerSettings server, CancellationToken cancellationToken);
}

/// <summary>Connects user-configured servers for both discovery and chat execution.</summary>
public sealed class ExternalMcpConnector(
    IGlobalSecretStore secrets,
    IFileSystem files,
    IPath paths,
    IToolEnvironment toolEnvironment) : IExternalMcpConnector
{
    public TimeSpan StartTimeout => TimeSpan.FromSeconds(45);

    public async Task<ExternalMcpConnection> ConnectAsync(
        McpServerSettings server, string? credential, CancellationToken cancellationToken)
    {
        var launch = await ResolveAsync(server, credential, cancellationToken);
        IClientTransport transport = launch.Endpoint is { } endpoint
            ? new HttpClientTransport(new HttpClientTransportOptions
            {
                Name = server.Name, Endpoint = endpoint, TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = string.IsNullOrWhiteSpace(launch.Credential) ? null
                    : new Dictionary<string, string> { ["Authorization"] = "Bearer " + launch.Credential }
            })
            : toolEnvironment.Launch(new ToolProcessLaunch(server.Name, launch.Command!, server.Arguments.ToArray(),
                launch.Directory, launch.Environment, InheritEnvironment: null, ShutdownTimeout: TimeSpan.FromMilliseconds(500)));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StartTimeout);
        // External stdio servers can take longer than the SDK's five-second discover probe to start.
        // Falling back to initialize on that same connection fails once a modern server begins serving it.
        var options = new McpClientOptions
        {
            DiscoverProbeTimeout = TimeSpan.FromSeconds(30),
            ProtocolVersion = server.ModernProtocolOnly ? "2026-07-28" : null
        };
        McpClient? client = null;
        try
        {
            client = await McpClient.CreateAsync(transport, options, cancellationToken: timeout.Token);
            var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
            return new ExternalMcpConnection(client, tools.Select(tool => tool.ProtocolTool).ToArray());
        }
        catch (Exception error)
        {
            if (client is not null) await client.DisposeAsync();
            // The start's own clock ran out. Passed on as a cancellation it read as a stop nobody
            // asked for, and nobody learned which server had kept the turn waiting.
            if (error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                throw new TimeoutException(
                    $"The MCP server '{server.Name}' did not start within {StartTimeout.TotalSeconds:0} seconds.", error);
            throw;
        }
    }

    public async Task<string> FingerprintAsync(McpServerSettings server, CancellationToken cancellationToken)
    {
        var launch = await ResolveAsync(server, null, cancellationToken);
        var identity = JsonSerializer.Serialize(new
        {
            server.Transport, launch.Command, server.Arguments, launch.Directory,
            Environment = launch.Environment.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray(),
            Endpoint = launch.Endpoint?.AbsoluteUri, launch.Credential, server.ModernProtocolOnly
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    /// <summary>What starting the server takes once its settings and saved secrets are resolved.</summary>
    private sealed record Launch(string? Command, string? Directory, IDictionary<string, string?> Environment,
        Uri? Endpoint, string? Credential);

    private async Task<Launch> ResolveAsync(McpServerSettings server, string? credential, CancellationToken cancellationToken)
    {
        if (!server.Enabled || server.Policy == "Deny")
            throw new InvalidOperationException("MCP server is disabled or denied; it was not started.");

        switch (server.Transport)
        {
            case "Stdio":
            {
                if (string.IsNullOrWhiteSpace(server.Command))
                    throw new ArgumentException("MCP command is required.");
                var directory = string.IsNullOrWhiteSpace(server.WorkingDirectory) ? null : server.WorkingDirectory.Trim();
                if (directory is not null && (!paths.IsFullyQualified(directory)
                                              || !await files.DirectoryExistsAsync(directory, cancellationToken)))
                    throw new ArgumentException("MCP working directory must be an existing absolute directory.");
                var command = server.Command.Trim();
                // ProcessStartInfo does not search WorkingDirectory for an unqualified executable.
                // Prefer a file in the configured directory, then let the OS search PATH.
                if (!paths.IsRooted(command) && directory is not null
                    && await files.FileExistsAsync(Path.Combine(directory, command), cancellationToken))
                    command = paths.GetFullPath(Path.Combine(directory, command));
                var saved = await secrets.GetAsync("mcp-env", server.Id, cancellationToken);
                var values = saved is null ? new Dictionary<string, string>()
                    : JsonSerializer.Deserialize<Dictionary<string, string>>(saved) ?? [];
                var environment = new Dictionary<string, string?>();
                foreach (var variable in server.EnvironmentVariables)
                {
                    var value = variable.Value;
                    if (variable.IsSecret && string.IsNullOrEmpty(value))
                    {
                        if (!values.TryGetValue(variable.Name, out value))
                            throw new InvalidOperationException($"MCP environment variable '{variable.Name}' needs a saved or entered value.");
                    }
                    environment[variable.Name] = value ?? string.Empty;
                }
                return new Launch(command, directory, environment, null, null);
            }
            case "StreamableHttp":
            {
                if (!Uri.TryCreate(server.Url, UriKind.Absolute, out var endpoint)
                    || endpoint.Scheme is not ("http" or "https"))
                    throw new ArgumentException("MCP URL must be an absolute HTTP or HTTPS URL.");
                credential = string.IsNullOrWhiteSpace(credential)
                    ? await secrets.GetAsync("mcp", server.Id, cancellationToken) : credential;
                return new Launch(null, null, new Dictionary<string, string?>(), endpoint, credential);
            }
            default:
                throw new ArgumentException($"Unsupported external MCP transport: {server.Transport}.");
        }
    }
}
