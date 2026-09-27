namespace AI.Application.Tools;

using System.Text.Json;

/// <summary>
/// Everything the MCP server said about a tool, kept apart from the function definition handed to
/// the model. The provider only needs name/description/input schema; the Host and the UI need the
/// rest — title, icons, safety hints, output schema — and previously lost all of it at this
/// boundary.
/// </summary>
public sealed record ToolDescriptor(
    string Name,
    string OriginalName,
    string? Title,
    string? Description,
    JsonElement InputSchema,
    JsonElement? OutputSchema,
    ToolAnnotations? Annotations,
    IReadOnlyList<ToolIcon> Icons,
    JsonElement? Meta)
{
    /// <summary>
    /// What to call this tool in the UI. MCP lets the title arrive in two places; the tool's own
    /// <c>title</c> wins over the one inside annotations, and the protocol name is the last resort.
    /// Never used for policy decisions — see <see cref="ToolAnnotations"/>.
    /// </summary>
    public string DisplayName => Coalesce(Title) ?? Coalesce(Annotations?.Title) ?? OriginalName;

    /// <summary>
    /// A descriptor for a server that advertised nothing beyond the protocol minimum — every
    /// optional field absent. Generic presentation has to work from exactly this much.
    /// </summary>
    public static ToolDescriptor Basic(string name, string originalName, string? description, JsonElement inputSchema) =>
        new(name, originalName, null, description, inputSchema, null, null, [], null);

    private static string? Coalesce(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// MCP tool annotations. The specification calls these hints: a server describes its own tool, so
/// for anything but the built-in server they are presentation input only. Host policy, directory
/// grants and approval prompts must never be derived from them.
/// </summary>
public sealed record ToolAnnotations(
    string? Title,
    bool? ReadOnlyHint,
    bool? DestructiveHint,
    bool? IdempotentHint,
    bool? OpenWorldHint);

public sealed record ToolIcon(string Source, string? MimeType, IReadOnlyList<string> Sizes);
