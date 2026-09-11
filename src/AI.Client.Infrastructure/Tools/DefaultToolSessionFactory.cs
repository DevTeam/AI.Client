namespace AI.Client.Infrastructure.Tools;

using Application.Tools;
using Contracts.Chat;
using Contracts.Tools;
using Contracts.Settings;
using Json.Schema;
using ModelContextProtocol.Client;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

public sealed class DefaultToolSessionFactory : IToolSessionFactory
{
    /// <summary>Name of the environment variable through which the built-in server receives its directory grants.</summary>
    public const string DirectoryGrantsVariable = "AI_CLIENT_DIRECTORY_GRANTS";

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
                var name = "mcp_built_in__" + tool.Name;
                // The provider sees only what a function definition may contain; title, icons,
                // annotations, output schema and _meta stay on this side for the Host and the UI.
                return new AgentTool(
                    new ChatToolDefinition(name, tool.Description ?? tool.Name, tool.InputSchema),
                    new ToolDescriptor(
                        name,
                        tool.Name,
                        tool.Title,
                        tool.Description,
                        tool.InputSchema,
                        tool.OutputSchema,
                        tool.Annotations is { } annotations
                            ? new Application.Tools.ToolAnnotations(annotations.Title, annotations.ReadOnlyHint,
                                annotations.DestructiveHint, annotations.IdempotentHint, annotations.OpenWorldHint)
                            : null,
                        tool.Icons?.Select(icon => new ToolIcon(icon.Source, icon.MimeType, icon.Sizes?.ToArray() ?? [])).ToArray() ?? [],
                        ToElement(tool.Meta)),
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

        public async Task<ToolCallResult> CallAsync(
            AgentTool tool,
            string arguments,
            IProgress<ToolProgress>? progress,
            CancellationToken cancellationToken)
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, object?>>(ValidateArguments(tool, arguments))!;
            // Passing a progress sink is what makes the client attach a progress token to the
            // request; without one the server is told not to report, so this stays null when
            // nobody is listening.
            var sink = progress is null ? null : new ProgressRelay(progress);
            var result = await _client.CallToolAsync(tool.OriginalName, values, sink, cancellationToken: cancellationToken);
            if (_descriptors[tool.OriginalName].OutputSchema is { } outputSchema
                && (result.StructuredContent is not { } content || !JsonSchema.Build(outputSchema).Evaluate(content).IsValid))
                throw new InvalidOperationException("Tool result does not match the output schema.");
            var blocks = (result.Content ?? []).Select(Describe).ToArray();
            return new ToolCallResult(blocks, result.StructuredContent, ToElement(result.Meta), result.IsError ?? false,
                ToolCallResult.ProjectForModel(blocks, result.StructuredContent, result.IsError ?? false));
        }

        // The protocol exposes _meta as a mutable JsonObject; the Application contracts take an
        // immutable JsonElement, so it is snapshotted here rather than shared across the boundary.
        private static JsonElement? ToElement(JsonObject? meta) =>
            meta is null ? null : JsonSerializer.SerializeToElement(meta);

        // Flattens a protocol content block to the parts presentation needs. An unrecognised block
        // type still arrives as ToolContentKind.Unknown rather than being dropped, so a server that
        // speaks a newer revision of the spec degrades instead of rendering as nothing.
        private static ToolContent Describe(ModelContextProtocol.Protocol.ContentBlock block) => block switch
        {
            ModelContextProtocol.Protocol.TextContentBlock text =>
                new ToolContent(ToolContentKind.Text, text.Text, null, null, null),
            ModelContextProtocol.Protocol.ImageContentBlock image =>
                new ToolContent(ToolContentKind.Image, null, image.MimeType, null, null),
            ModelContextProtocol.Protocol.AudioContentBlock audio =>
                new ToolContent(ToolContentKind.Audio, null, audio.MimeType, null, null),
            ModelContextProtocol.Protocol.ResourceLinkBlock link =>
                new ToolContent(ToolContentKind.ResourceLink, null, link.MimeType, link.Uri, link.Name),
            ModelContextProtocol.Protocol.EmbeddedResourceBlock embedded =>
                new ToolContent(ToolContentKind.Resource,
                    (embedded.Resource as ModelContextProtocol.Protocol.TextResourceContents)?.Text,
                    embedded.Resource?.MimeType, embedded.Resource?.Uri, null),
            _ => new ToolContent(ToolContentKindExtensions.FromWireType(block.Type), null, null, null, null),
        };
        /// <summary>Adapts the protocol's progress notifications to the Host's own shape.</summary>
        private sealed class ProgressRelay(IProgress<ToolProgress> target)
            : IProgress<ModelContextProtocol.ProgressNotificationValue>
        {
            public void Report(ModelContextProtocol.ProgressNotificationValue value) =>
                target.Report(new ToolProgress(value.Progress, value.Total, value.Message));
        }

        public ValueTask DisposeAsync() => _client.DisposeAsync();
    }
}
