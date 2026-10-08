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
public sealed class DefaultToolSessionFactory(IToolResultModelProjector modelProjector, IChatTemporaryDirectory temporaryDirectory) : IMcpServerConnection
{
    public Guid ServerId => DefaultMcpServer.Id;

    /// <summary>Name of the environment variable through which the built-in server receives its directory grants.</summary>
    public const string DirectoryGrantsVariable = "AI_CLIENT_DIRECTORY_GRANTS";

    public async Task<IToolSession> OpenAsync(IReadOnlyList<ToolDirectoryGrant> directoryGrants, ToolRunContext run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directoryGrants);
        var executable = Path.Combine(AppContext.BaseDirectory, "mcp", "AI.Mcp.BuiltIn" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        foreach (var name in new[] { "SystemRoot", "WINDIR", "TEMP", "TMP", "LOCALAPPDATA", "APPDATA", "PATHEXT", "DOTNET_ROOT",
                     "PROCESSOR_ARCHITECTURE", "PROCESSOR_ARCHITEW6432", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "ProgramData", "ALLUSERSPROFILE", "COMSPEC", "HOMEDRIVE", "HOMEPATH" })
            if (Environment.GetEnvironmentVariable(name) is { } value) environment[name] = value;
        var effectiveGrants = directoryGrants.ToList();
        if (run.ProjectId != Guid.Empty && run.ChatId != Guid.Empty)
            effectiveGrants.Add(new ToolDirectoryGrant(temporaryDirectory.GetOrCreate(run.ProjectId, run.ChatId),
                true, ["read", "write", "edit", "delete"]));
        environment[DirectoryGrantsVariable] = JsonSerializer.Serialize(
            effectiveGrants.Select(grant => new { root = grant.Root, recursive = grant.Recursive, capabilities = grant.Capabilities }));
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "Default tools", Command = executable,
            InheritEnvironmentVariables = false,
            EnvironmentVariables = environment,
            // Closing the session waits this long for the process to exit, and the run's answer is
            // not saved until it is closed. At the SDK's 5 s default every turn that used a tool sat
            // finished but unsaved for five seconds, its answer shown as a live copy that was then
            // replaced. Every call has returned by then, so there is nothing left to wait for.
            ShutdownTimeout = TimeSpan.FromMilliseconds(500)
        });
        var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        try
        {
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            return new McpToolSession(client, tools.Select(tool => tool.ProtocolTool).ToArray(),
                DefaultMcpServer.Id, ToolRef.BuiltInPrefix, canonicalizePaths: true, modelProjector);
        }
        catch { await client.DisposeAsync(); throw; }
    }
}
