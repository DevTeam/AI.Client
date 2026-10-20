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
    // MCP schemas may include extension annotations such as x-mcp-header. They do not change
    // argument validation, but must remain in the schema passed to the model and MCP client.
    private static readonly BuildOptions McpSchemaBuildOptions = new()
    {
        Dialect = Dialect.Draft202012.With([], allowUnknownKeywords: true)
    };

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
            _ = BuildSchema(tool.InputSchema);
            if (tool.OutputSchema is { } outputSchema) _ = BuildSchema(outputSchema);
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
        if (arguments.Length > ToolCallLimits.ArgumentCharacters)
            throw new ArgumentException($"Tool arguments exceed the {ToolCallLimits.ArgumentCharacters}-character limit. "
                + "Split the content across several calls.", nameof(arguments));
        var schema = _descriptors[tool.OriginalName].InputSchema;
        var canonical = JsonNode.Parse(arguments) is JsonObject parsed ? Repair(parsed, schema) : null;
        var input = canonical is null
            ? JsonSerializer.Deserialize<JsonElement>(arguments)
            : JsonSerializer.SerializeToElement(canonical);
        var evaluation = BuildSchema(schema)
            .Evaluate(input, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (!evaluation.IsValid || canonical is null)
            throw new ArgumentException("Tool arguments do not match the input schema. " + Explain(evaluation, "the arguments"), nameof(arguments));
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
    /// Undoes the slips models make most often with otherwise correct calls: an object or array
    /// sent as its JSON text, an enum value in the wrong case, and a number past its bound. Only top-level properties whose
    /// schema leaves no other reading are changed, so a value that was meant as a string stays one.
    /// </summary>
    private static JsonObject Repair(JsonObject arguments, JsonElement schema)
    {
        if (!schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
            return arguments;
        foreach (var property in properties.EnumerateObject())
        {
            if (arguments[property.Name] is JsonValue number && number.GetValueKind() == JsonValueKind.Number
                && Clamp(number.GetValue<double>(), property.Value) is { } bounded)
            {
                arguments[property.Name] = bounded;
                continue;
            }
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

    /// <summary>
    /// A number past its schema's inclusive bound, brought back to that bound, or null when it is
    /// within them. The bounds are caps on how much a call returns or waits, so the bound is what
    /// the tool would have done anyway; a refusal instead cost the model a whole step to resend
    /// the call with the same intent.
    /// </summary>
    private static JsonValue? Clamp(double number, JsonElement schema)
    {
        var bounded = number;
        if (schema.TryGetProperty("maximum", out var maximum) && maximum.ValueKind == JsonValueKind.Number)
            bounded = Math.Min(bounded, maximum.GetDouble());
        if (schema.TryGetProperty("minimum", out var minimum) && minimum.ValueKind == JsonValueKind.Number)
            bounded = Math.Max(bounded, minimum.GetDouble());
        if (bounded.Equals(number)) return null;
        return Types(schema).Contains("integer") ? JsonValue.Create((long)bounded) : JsonValue.Create(bounded);
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
    private static string Explain(EvaluationResults evaluation, string root)
    {
        var problems = Failures(evaluation)
            .Select(detail => string.Join("; ", detail.Errors!.Select(error =>
                $"{Where(detail.InstanceLocation, root)}: {error.Value}")))
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

    /// <summary>Names the offending property, or the checked object itself when the fault is at its root.</summary>
    private static string Where(JsonPointer location, string root)
    {
        var path = location.ToString();
        return string.IsNullOrEmpty(path) || path == "/" ? root : path.TrimStart('/').Replace('/', '.');
    }

    /// <summary>Why a successful result breaks the tool's output schema, or null when it does not.</summary>
    private static string? OutputMismatch(JsonElement outputSchema, JsonElement? structured)
    {
        if (structured is not { } content) return "It returned no structured content.";
        var evaluation = BuildSchema(outputSchema).Evaluate(content, new EvaluationOptions { OutputFormat = OutputFormat.List });
        return evaluation.IsValid ? null : Explain(evaluation, "the result");
    }

    private static string Absolute(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException($"Path must be absolute: {path}", nameof(path));
        return Path.GetFullPath(path);
    }

    private static JsonSchema BuildSchema(JsonElement schema) => JsonSchema.Build(schema, McpSchemaBuildOptions);

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
        ModelContextProtocol.Protocol.CallToolResult result;
        try
        {
            result = await _client.CallToolAsync(tool.OriginalName, values, sink, cancellationToken: cancellationToken);
        }
        // The server answered with a JSON-RPC error, such as a Jenkins job that does not exist. The
        // transport worked and the outcome is known, so this is the call's result, not a failure of
        // the turn: the model reads it and corrects the call instead of the whole run ending.
        catch (ModelContextProtocol.McpProtocolException error)
        {
            var rejection = new[] { ToolContent.OfText(error.Message) };
            return new ToolCallResult(rejection, null, null, true, _modelProjector.Project(rejection, null, true));
        }
        var blocks = (result.Content ?? []).Select(Describe).ToArray();
        // MCP tool errors may contain only text; the output schema describes successful results.
        if (result.IsError != true && _descriptors[tool.OriginalName].OutputSchema is { } outputSchema
            && OutputMismatch(outputSchema, result.StructuredContent) is { } mismatch)
        {
            // The tool ran and its effects stand; only its answer is malformed. That is the call's
            // result rather than a broken transport, so the model is told what came back and why it
            // cannot be trusted, and the turn goes on. The structured part is the faulty one and is
            // left out; the text blocks are kept, since they are often all the model needs.
            var faulty = blocks.Prepend(ToolContent.OfText(
                "The tool ran, but its result does not match its declared output schema, so it may be incomplete. "
                + $"{mismatch} Its effects have occurred; do not automatically repeat it.")).ToArray();
            return new ToolCallResult(faulty, null, ToElement(result.Meta), true, _modelProjector.Project(faulty, null, true));
        }
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
            new ToolContent(ToolContentKind.Image, null, image.MimeType, null, null, Data: ImageBytes(image)),
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

    // The block's Data member holds the Base64 characters as sent on the wire, not the bytes;
    // DecodedData decodes them. A server that sends something that is not Base64 would throw
    // here, and a hostile or simply broken server must not be able to fail the whole tool call:
    // the block is then passed on without bytes, which is how an unrecognised block degrades.
    private static byte[]? ImageBytes(ModelContextProtocol.Protocol.ImageContentBlock image)
    {
        try
        {
            return image.DecodedData.ToArray();
        }
        catch (FormatException)
        {
            return null;
        }
    }

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
