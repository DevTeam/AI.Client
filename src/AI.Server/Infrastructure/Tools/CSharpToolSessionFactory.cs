namespace AI.Infrastructure.Tools;

using AI.Application.Tools;
using AI.Contracts.FileSystem;
using AI.Contracts.Settings;
using AI.Contracts.Tools;
using ModelContextProtocol.Client;

/// <summary>
/// Connects to the optional C# scripting MCP server, which is shipped as a separate executable
/// in the application directory on Windows or in a shared companion directory on macOS/Linux.
/// When the binary is absent
/// <see cref="OpenAsync"/> throws <see cref="FileNotFoundException"/>; the settings UI surfaces
/// that as the server being uninstalled.
/// </summary>
public sealed class CSharpToolSessionFactory(IFileSystem files, IToolResultModelProjector modelProjector) : IMcpServerConnection
{
    public Guid ServerId => CSharpMcpServer.Id;

    /// <summary>
    /// Directory grants mean nothing here: this server reaches whatever its own process can,
    /// not a project's file system. The run is also unused for the same reason — the server is
    /// a stateless scripting box, not a participant in this conversation.
    /// </summary>
    public async Task<IToolSession> OpenAsync(IReadOnlyList<ToolDirectoryGrant> directoryGrants, ToolRunContext run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directoryGrants);
        var directory = Path.Combine(AppContext.BaseDirectory, "mcp-csharp");
        var executable = Path.Combine(directory, "AI.Mcp.CSharp" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        if (!await files.FileExistsAsync(executable, cancellationToken) && !OperatingSystem.IsWindows())
        {
            directory = OperatingSystem.IsMacOS()
                ? "/Library/Application Support/AI Client/McpCsharp"
                : "/opt/ai-client-csharp-mcp";
            executable = Path.Combine(directory, "AI.Mcp.CSharp");
        }
        if (!await files.FileExistsAsync(executable, cancellationToken))
            throw new FileNotFoundException(
                "The C# scripting MCP server is not installed. Choose \"C# scripting tools\" in the Host or Desktop installer, or install the C# MCP companion package on Linux.",
                executable);

        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        foreach (var name in new[] { "SystemRoot", "WINDIR", "TEMP", "TMP", "LOCALAPPDATA", "APPDATA", "PATHEXT", "DOTNET_ROOT",
                     "PROCESSOR_ARCHITECTURE", "PROCESSOR_ARCHITEW6432", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "ProgramData", "ALLUSERSPROFILE", "COMSPEC", "HOMEDRIVE", "HOMEPATH" })
            if (Environment.GetEnvironmentVariable(name) is { } value) environment[name] = value;

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "C# scripts",
            Command = executable,
            InheritEnvironmentVariables = false,
            EnvironmentVariables = environment,
            ShutdownTimeout = TimeSpan.FromMilliseconds(500)
        });
        var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        try
        {
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            return new McpToolSession(client, tools.Select(tool => tool.ProtocolTool).ToArray(),
                CSharpMcpServer.Id, ToolRef.CSharpPrefix, canonicalizePaths: false, modelProjector);
        }
        catch { await client.DisposeAsync(); throw; }
    }
}
