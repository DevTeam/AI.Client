namespace AI.Infrastructure.Tools;

using Application.Tools;
using Contracts.Settings;
using Contracts.Tools;
using ModelContextProtocol.Client;
using System.Text.Json;

/// <summary>
/// Connects to the built-in tool server, which ships alongside the application and runs as a child
/// process over stdio.
/// </summary>
public sealed class DefaultToolSessionFactory(
    IToolResultModelProjector modelProjector,
    IChatTemporaryDirectory temporaryDirectory,
    IToolEnvironment toolEnvironment) : IMcpServerConnection
{
    private const string ProtocolVersion = "2026-07-28";

    public Guid ServerId => DefaultMcpServer.Id;

    /// <summary>Name of the environment variable through which the built-in server receives its directory grants.</summary>
    public const string DirectoryGrantsVariable = "AI_CLIENT_DIRECTORY_GRANTS";

    public async Task<IToolSession> OpenAsync(IReadOnlyList<ToolDirectoryGrant> directoryGrants, ToolRunContext run, CancellationToken cancellationToken)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "mcp", "AI.Mcp.BuiltIn" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        var environment = CreateEnvironment(directoryGrants, run);
        // Closing the session waits ShutdownTimeout for the process to exit, and the run's answer is
        // not saved until it is closed. At the SDK's 5 s default every turn that used a tool sat
        // finished but unsaved for five seconds, its answer shown as a live copy that was then
        // replaced. Closing the server also stops managed command trees; callers must finish
        // waiting on their handles before reporting verification.
        var transport = toolEnvironment.Launch(new ToolProcessLaunch("Default tools", executable, [], null, environment,
            InheritEnvironment: false, ShutdownTimeout: TimeSpan.FromMilliseconds(500)));
        // This is our own current server, so do not let a slow discovery response make the SDK
        // fall back to the legacy initialize handshake on the same connection.
        var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            ProtocolVersion = ProtocolVersion,
            DiscoverProbeTimeout = TimeSpan.FromSeconds(30)
        }, cancellationToken: cancellationToken);
        try
        {
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            return new McpToolSession(client, tools.Select(tool => tool.ProtocolTool).ToArray(),
                DefaultMcpServer.Id, ToolRef.BuiltInPrefix, canonicalizePaths: true, modelProjector);
        }
        catch { await client.DisposeAsync(); throw; }
    }

    /// <summary>
    /// The whole environment the built-in server process starts with: a minimal inherited set and the
    /// directory grants, the project's and the chat's temporary directory, which are also all its
    /// commands and scripts may reach: they always run in the sandbox.
    /// </summary>
    public IDictionary<string, string?> CreateEnvironment(IReadOnlyList<ToolDirectoryGrant> directoryGrants, ToolRunContext run)
    {
        ArgumentNullException.ThrowIfNull(directoryGrants);
        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        foreach (var name in new[] { "SystemRoot", "WINDIR", "TEMP", "TMP", "LOCALAPPDATA", "APPDATA", "PATHEXT", "DOTNET_ROOT",
                     "PROCESSOR_ARCHITECTURE", "PROCESSOR_ARCHITEW6432", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "ProgramData", "ALLUSERSPROFILE", "COMSPEC", "HOMEDRIVE", "HOMEPATH" })
            if (Environment.GetEnvironmentVariable(name) is { } value) environment[name] = value;
        var effectiveGrants = directoryGrants.Select(grant => new
        {
            root = grant.Root, recursive = grant.Recursive, capabilities = grant.Capabilities, purpose = "project"
        }).ToList();
        if (run.ProjectId != Guid.Empty && run.ChatId != Guid.Empty)
            effectiveGrants.Add(new
            {
                root = temporaryDirectory.GetOrCreate(run.ProjectId, run.ChatId), recursive = true,
                capabilities = (IReadOnlyList<string>)["read", "write", "edit", "delete"], purpose = "chatTemporary"
            });
        environment[DirectoryGrantsVariable] = JsonSerializer.Serialize(effectiveGrants);
        return environment;
    }
}
