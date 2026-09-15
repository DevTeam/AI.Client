namespace AI.Client.Infrastructure.Tools;

using Application.Tools;
using Contracts.Chat;
using Contracts.Tools;
using Json.Pointer;
using Json.Schema;
using ModelContextProtocol.Client;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// One connected MCP server, seen as a set of <see cref="AgentTool"/>. Everything here is protocol
/// work — listing tools, validating arguments against the declared input schema, checking results
/// against the output schema — so it is shared by every server the Host connects to, whatever its
/// transport.
/// </summary>
public sealed class McpToolSession : IToolSession
{
    /// <summary>
    /// Properties the Host canonicalizes before showing them to the user. Only a server that acts
    /// on the file system declares them, which is why this is a per-session choice rather than
    /// something applied to every server's arguments.
    /// </summary>
    private static readonly string[] PathProperties = ["workingDirectory", "path", "paths", "source", "destination"];

    private readonly McpClient _client;
    private readonly Dictionary<string, ModelContextProtocol.Protocol.Tool> _descriptors;
    private readonly bool _canonicalizePaths;
    private readonly Func<ValueTask>? _shutdown;

    public McpToolSession(
        McpClient client,
        IReadOnlyList<ModelContextProtocol.Protocol.Tool> descriptors,
        Guid serverId,
        string namePrefix,
        bool canonicalizePaths,
        Func<ValueTask>? shutdown = null)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        _client = client;
        _canonicalizePaths = canonicalizePaths;
        _shutdown = shutdown;
        _descriptors = descriptors.ToDictionary(tool => tool.Name);
        Tools = descriptors.Select(tool =>
        {
            _ = JsonSchema.Build(tool.InputSchema);
            if (tool.OutputSchema is { } outputSchema) _ = JsonSchema.Build(outputSchema);
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                tool.InputSchema.GetRawText() + tool.OutputSchema?.GetRawText())));
            var name = namePrefix + tool.Name;
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
                serverId, tool.Name, hash);
        }).ToArray();
    }

    public IReadOnlyList<AgentTool> Tools { get; }

    public string ValidateArguments(AgentTool tool, string arguments)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (arguments.Length > 65536) throw new ArgumentException("Tool arguments exceed the size limit.", nameof(arguments));
        var input = JsonSerializer.Deserialize<JsonElement>(arguments);
        var evaluation = JsonSchema.Build(_descriptors[tool.OriginalName].InputSchema)
            .Evaluate(input, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (!evaluation.IsValid)
            throw new ArgumentException("Tool arguments do not match the input schema. " + Explain(evaluation), nameof(arguments));
        var canonical = JsonNode.Parse(arguments)!.AsObject();
        if (!_canonicalizePaths) return canonical.ToJsonString();
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

    /// <summary>
    /// Turns a schema evaluation into something the model can act on. "Arguments do not match the
    /// schema" tells a caller nothing it did not already suspect; the property that failed and why
    /// is what lets it fix the call instead of guessing.
    /// </summary>
    private static string Explain(EvaluationResults evaluation)
    {
        var problems = Failures(evaluation)
            .Select(detail => string.Join("; ", detail.Errors!.Select(error =>
                $"{Where(detail.InstanceLocation)}: {error.Value}")))
            .Where(text => text.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToArray();
        return problems.Length == 0 ? "No further detail was reported." : string.Join(" ", problems);
    }

    private static IEnumerable<EvaluationResults> Failures(EvaluationResults evaluation)
    {
        if (evaluation.Errors is { Count: > 0 }) yield return evaluation;
        foreach (var nested in evaluation.Details ?? [])
            foreach (var failure in Failures(nested))
                yield return failure;
    }

    /// <summary>Names the offending property, or the argument object itself when the fault is at its root.</summary>
    private static string Where(JsonPointer location)
    {
        var path = location.ToString();
        return string.IsNullOrEmpty(path) || path == "/" ? "the arguments" : path.TrimStart('/').Replace('/', '.');
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
        ArgumentNullException.ThrowIfNull(tool);
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

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        // An in-process server has to be torn down with its client; a child process ends on its own
        // when the transport closes, so it supplies nothing here.
        if (_shutdown is not null) await _shutdown();
    }
}
