namespace AI.Client.Infrastructure.Tools;

using Application.Tools;
using Contracts.Chat;
using Contracts.Settings;
using Json.Schema;
using ModelContextProtocol.Client;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

public sealed class DefaultToolSessionFactory : IToolSessionFactory
{
    public async Task<IToolSession> OpenAsync(CancellationToken cancellationToken)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "mcp", "AI.Client.Mcp.BuiltIn" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        foreach (var name in new[] { "SystemRoot", "WINDIR", "TEMP", "TMP", "LOCALAPPDATA", "APPDATA", "PATHEXT", "DOTNET_ROOT",
                     "PROCESSOR_ARCHITECTURE", "PROCESSOR_ARCHITEW6432", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "ProgramData", "ALLUSERSPROFILE", "COMSPEC", "HOMEDRIVE", "HOMEPATH" })
            if (Environment.GetEnvironmentVariable(name) is { } value) environment[name] = value;
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "Default tools", Command = executable,
            InheritEnvironmentVariables = false,
            EnvironmentVariables = environment
        });
        var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        try
        {
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            return new Session(client, tools.Select(tool => tool.ProtocolTool).ToArray());
        }
        catch { await client.DisposeAsync(); throw; }
    }

    private sealed class Session : IToolSession
    {
        private readonly McpClient _client;
        private readonly Dictionary<string, ModelContextProtocol.Protocol.Tool> _descriptors;
        public Session(McpClient client, ModelContextProtocol.Protocol.Tool[] descriptors)
        {
            _client = client;
            _descriptors = descriptors.ToDictionary(tool => tool.Name);
            Tools = descriptors.Select(tool =>
            {
                _ = JsonSchema.Build(tool.InputSchema);
                if (tool.OutputSchema is { } outputSchema) _ = JsonSchema.Build(outputSchema);
                var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                    tool.InputSchema.GetRawText() + tool.OutputSchema?.GetRawText())));
                return new AgentTool(new ChatToolDefinition("mcp_default__" + tool.Name, tool.Description ?? tool.Name, tool.InputSchema),
                    DefaultMcpServer.Id, tool.Name, hash);
            }).ToArray();
        }
        public IReadOnlyList<AgentTool> Tools { get; }

        public string ValidateArguments(AgentTool tool, string arguments)
        {
            if (arguments.Length > 65536) throw new ArgumentException("Tool arguments exceed the size limit.", nameof(arguments));
            var input = JsonSerializer.Deserialize<JsonElement>(arguments);
            if (!JsonSchema.Build(_descriptors[tool.OriginalName].InputSchema).Evaluate(input).IsValid)
                throw new ArgumentException("Tool arguments do not match the input schema.", nameof(arguments));
            var canonical = JsonNode.Parse(arguments)!.AsObject();
            if (tool.OriginalName == "process_run" && canonical["workingDirectory"] is { } workingDirectory)
            {
                var path = workingDirectory.GetValue<string>();
                if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Working directory must be an absolute path.", nameof(arguments));
                canonical["workingDirectory"] = Path.GetFullPath(path);
            }
            return canonical.ToJsonString();
        }

        public async Task<string> CallAsync(AgentTool tool, string arguments, CancellationToken cancellationToken)
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, object?>>(ValidateArguments(tool, arguments))!;
            var result = await _client.CallToolAsync(tool.OriginalName, values, cancellationToken: cancellationToken);
            if (_descriptors[tool.OriginalName].OutputSchema is { } outputSchema
                && (result.StructuredContent is not { } content || !JsonSchema.Build(outputSchema).Evaluate(content).IsValid))
                throw new InvalidOperationException("Tool result does not match the output schema.");
            return JsonSerializer.Serialize(result);
        }
        public ValueTask DisposeAsync() => _client.DisposeAsync();
    }
}
