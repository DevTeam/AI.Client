namespace AI.Infrastructure.Tools;

using Application.Settings;
using Application.Tools;
using Contracts.Settings;
using Contracts.Tools;
using ModelContextProtocol.Client;
using System.Text.Json;

/// <summary>Connects user-configured servers for both discovery and chat execution.</summary>
public sealed class ExternalToolSessionFactory(
    IGlobalSecretStore secrets,
    IToolResultModelProjector modelProjector)
{
    public async Task<IToolSession> OpenAsync(
        McpServerSettings server, CancellationToken cancellationToken, string? credential = null)
    {
        if (!server.Enabled || server.Policy == "Deny")
            throw new InvalidOperationException("MCP server is disabled or denied; it was not started.");

        IClientTransport transport;
        switch (server.Transport)
        {
            case "Stdio":
            {
                if (string.IsNullOrWhiteSpace(server.Command))
                    throw new ArgumentException("MCP command is required.");
                var directory = string.IsNullOrWhiteSpace(server.WorkingDirectory) ? null : server.WorkingDirectory.Trim();
                if (directory is not null && (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)))
                    throw new ArgumentException("MCP working directory must be an existing absolute directory.");
                var command = server.Command.Trim();
                // ProcessStartInfo does not search WorkingDirectory for an unqualified executable.
                // Prefer a file in the configured directory, then let the OS search PATH.
                if (!Path.IsPathRooted(command) && directory is not null
                    && File.Exists(Path.Combine(directory, command)))
                    command = Path.GetFullPath(Path.Combine(directory, command));
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
                transport = new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = server.Name, Command = command, Arguments = server.Arguments.ToArray(),
                    WorkingDirectory = directory, EnvironmentVariables = environment,
                    ShutdownTimeout = TimeSpan.FromMilliseconds(500)
                });
                break;
            }
            case "StreamableHttp":
            {
                if (!Uri.TryCreate(server.Url, UriKind.Absolute, out var endpoint)
                    || endpoint.Scheme is not ("http" or "https"))
                    throw new ArgumentException("MCP URL must be an absolute HTTP or HTTPS URL.");
                credential = string.IsNullOrWhiteSpace(credential)
                    ? await secrets.GetAsync("mcp", server.Id, cancellationToken) : credential;
                transport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Name = server.Name, Endpoint = endpoint, TransportMode = HttpTransportMode.StreamableHttp,
                    AdditionalHeaders = string.IsNullOrWhiteSpace(credential) ? null
                        : new Dictionary<string, string> { ["Authorization"] = "Bearer " + credential }
                });
                break;
            }
            default:
                throw new ArgumentException($"Unsupported external MCP transport: {server.Transport}.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
        try
        {
            var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
            return new McpToolSession(client, tools.Select(tool => tool.ProtocolTool).ToArray(),
                server.Id, $"mcp_{server.Id:N}__", canonicalizePaths: false, modelProjector);
        }
        catch { await client.DisposeAsync(); throw; }
    }
}
