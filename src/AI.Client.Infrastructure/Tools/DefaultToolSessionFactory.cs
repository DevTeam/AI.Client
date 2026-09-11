namespace AI.Client.Infrastructure.Tools;

using Application.Tools;
using Contracts.Chat;
using Contracts.Settings;
using Json.Schema;
using ModelContextProtocol.Client;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

public sealed class DefaultToolSessionFactory : IToolSessionFactory
{
    /// <summary>Name of the environment variable through which the built-in server receives its directory grants.</summary>
    public const string DirectoryGrantsVariable = "AI_CLIENT_DIRECTORY_GRANTS";

    // This is the tool result string a model actually reads back on every following turn — not
    // markup rendered into a browser — so the default encoder's blanket escaping of non-ASCII
    // text (`\uXXXX` per character) is pure waste here, and it compounds with ToolReply's own use
    // of the same relaxed encoder one layer in: a tool result already free of that escaping would
    // otherwise get it reintroduced right back by this second, outer serialization pass.
    private static readonly JsonSerializerOptions ModelFacingJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<IToolSession> OpenAsync(IReadOnlyList<ToolDirectoryGrant> directoryGrants, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directoryGrants);
        var executable = Path.Combine(AppContext.BaseDirectory, "mcp", "AI.Client.Mcp.BuiltIn" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        foreach (var name in new[] { "SystemRoot", "WINDIR", "TEMP", "TMP", "LOCALAPPDATA", "APPDATA", "PATHEXT", "DOTNET_ROOT",
                     "PROCESSOR_ARCHITECTURE", "PROCESSOR_ARCHITEW6432", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "ProgramData", "ALLUSERSPROFILE", "COMSPEC", "HOMEDRIVE", "HOMEPATH" })
            if (Environment.GetEnvironmentVariable(name) is { } value) environment[name] = value;
        // File system tools stay closed unless the project granted a directory, so an empty set is passed through as such.
        environment[DirectoryGrantsVariable] = JsonSerializer.Serialize(
            directoryGrants.Select(grant => new { root = grant.Root, recursive = grant.Recursive, capabilities = grant.Capabilities }));
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
        private static readonly string[] PathProperties = ["workingDirectory", "path", "paths", "source", "destination"];
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
                return new AgentTool(new ChatToolDefinition("mcp_built_in__" + tool.Name, tool.Description ?? tool.Name, tool.InputSchema),
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
            // Canonicalize before approval so the user and the server judge the same path.
            foreach (var property in PathProperties)
            {
                if (canonical[property] is not { } node) continue;
                if (node is JsonArray array)
                {
                    for (var index = 0; index < array.Count; index++) array[index] = Absolute(array[index]?.GetValue<string>());
                }
                else canonical[property] = Absolute(node.GetValue<string>());
            }
            return canonical.ToJsonString();
        }

        private static string Absolute(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                throw new ArgumentException($"Path must be absolute: {path}", nameof(path));
            return Path.GetFullPath(path);
        }

        public async Task<string> CallAsync(AgentTool tool, string arguments, CancellationToken cancellationToken)
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, object?>>(ValidateArguments(tool, arguments))!;
            var result = await _client.CallToolAsync(tool.OriginalName, values, cancellationToken: cancellationToken);
            if (_descriptors[tool.OriginalName].OutputSchema is { } outputSchema
                && (result.StructuredContent is not { } content || !JsonSchema.Build(outputSchema).Evaluate(content).IsValid))
                throw new InvalidOperationException("Tool result does not match the output schema.");
            return JsonSerializer.Serialize(result, ModelFacingJson);
        }
        public ValueTask DisposeAsync() => _client.DisposeAsync();
    }
}
