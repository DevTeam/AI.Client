namespace AI.Infrastructure.Tools;

using Application.Chat;
using Application.Tools;
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
    private readonly IToolResultModelProjector _modelProjector;
    private readonly Func<ValueTask>? _shutdown;

    public McpToolSession(
        McpClient client,
        IReadOnlyList<ModelContextProtocol.Protocol.Tool> descriptors,
        Guid serverId,
        string namePrefix,
        bool canonicalizePaths,
        IToolResultModelProjector modelProjector,
        Func<ValueTask>? shutdown = null)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        _client = client;
        _canonicalizePaths = canonicalizePaths;
        _modelProjector = modelProjector;
        _shutdown = shutdown;
        _descriptors = descriptors.ToDictionary(tool => tool.Name);
        Tools = descriptors.Select(tool =>
        {
            _ = JsonSchema.Build(tool.InputSchema);
            if (tool.OutputSchema is { } outputSchema) _ = JsonSchema.Build(outputSchema);
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                tool.InputSchema.GetRawText() + tool.OutputSchema?.GetRawText())));
            var name = ModelName(namePrefix, tool.Name);
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

    // Provider function names are limited to 64 ASCII letters, digits, underscores and hyphens.
    // Keep the full server identity and hash names that need shortening or character repair;
    // calls still use OriginalName, exactly as declared by the MCP server.
    private static string ModelName(string prefix, string original)
    {
        var name = prefix + original;
        if (name.Length <= 64 && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
            return name;
        var safe = new string(original.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-' ? character : '_').ToArray());
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(original)))[..12];
        return prefix + safe[..Math.Min(safe.Length, 64 - prefix.Length - hash.Length - 1)] + "_" + hash;
    }

    public string ValidateArguments(AgentTool tool, string arguments)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (arguments.Length > 65536) throw new ArgumentException("Tool arguments exceed the size limit.", nameof(arguments));
        var schema = _descriptors[tool.OriginalName].InputSchema;
        var canonical = JsonNode.Parse(arguments) is JsonObject parsed ? Repair(parsed, schema) : null;
        var input = canonical is null
            ? JsonSerializer.Deserialize<JsonElement>(arguments)
            : JsonSerializer.SerializeToElement(canonical);
        var evaluation = JsonSchema.Build(schema)
            .Evaluate(input, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (!evaluation.IsValid || canonical is null)
            throw new ArgumentException("Tool arguments do not match the input schema. " + Explain(evaluation), nameof(arguments));
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
    /// Undoes the two slips models make most often with otherwise correct calls: an object or array
    /// sent as its JSON text, and an enum value in the wrong case. Only top-level properties whose
    /// schema leaves no other reading are changed, so a value that was meant as a string stays one.
    /// </summary>
    private static JsonObject Repair(JsonObject arguments, JsonElement schema)
    {
        if (!schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
            return arguments;
        foreach (var property in properties.EnumerateObject())
        {
            if (arguments[property.Name] is not JsonValue value || !value.TryGetValue<string>(out var text)) continue;
            var types = Types(property.Value).ToHashSet(StringComparer.Ordinal);
            if (!types.Contains("string") && (types.Contains("object") || types.Contains("array")))
            {
                try
                {
                    if (JsonNode.Parse(text) is { } node
                        && (node is JsonObject && types.Contains("object") || node is JsonArray && types.Contains("array")))
                        arguments[property.Name] = node;
                }
                catch (JsonException)
                {
                    // Not JSON after all: leave it for the schema to reject with its own message.
                }
                continue;
            }
            var choices = Enum(property.Value).ToArray();
            if (choices.Length > 0 && !choices.Contains(text, StringComparer.Ordinal)
                && choices.SingleOrDefault(choice => string.Equals(choice, text, StringComparison.OrdinalIgnoreCase)) is { } match)
                arguments[property.Name] = match;
        }
        return arguments;
    }

    private static IEnumerable<string> Types(JsonElement schema)
    {
        if (schema.TryGetProperty("type", out var type))
        {
            if (type.ValueKind == JsonValueKind.String) yield return type.GetString()!;
            else if (type.ValueKind == JsonValueKind.Array)
                foreach (var item in type.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String) yield return item.GetString()!;
        }
        foreach (var keyword in new[] { "anyOf", "oneOf" })
            if (schema.TryGetProperty(keyword, out var options) && options.ValueKind == JsonValueKind.Array)
                foreach (var option in options.EnumerateArray())
                    foreach (var nested in Types(option))
                        yield return nested;
    }

    private static IEnumerable<string> Enum(JsonElement schema)
    {
        if (schema.TryGetProperty("enum", out var values) && values.ValueKind == JsonValueKind.Array)
            foreach (var value in values.EnumerateArray())
                if (value.ValueKind == JsonValueKind.String) yield return value.GetString()!;
        foreach (var keyword in new[] { "anyOf", "oneOf" })
            if (schema.TryGetProperty(keyword, out var options) && options.ValueKind == JsonValueKind.Array)
                foreach (var option in options.EnumerateArray())
                    foreach (var nested in Enum(option))
                        yield return nested;
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
        // MCP tool errors may contain only text; the output schema describes successful results.
        if (result.IsError != true && _descriptors[tool.OriginalName].OutputSchema is { } outputSchema
            && (result.StructuredContent is not { } content || !JsonSchema.Build(outputSchema).Evaluate(content).IsValid))
            throw new InvalidOperationException("Tool result does not match the output schema.");
        var blocks = (result.Content ?? []).Select(Describe).ToArray();
        return new ToolCallResult(blocks, result.StructuredContent, ToElement(result.Meta), result.IsError ?? false,
            _modelProjector.Project(blocks, result.StructuredContent, result.IsError ?? false));
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
            new ToolContent(ToolContentKind.Image, null, image.MimeType, null, null, Data: image.Data.ToArray()),
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
