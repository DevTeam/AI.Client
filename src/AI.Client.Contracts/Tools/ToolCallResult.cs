namespace AI.Client.Contracts.Tools;

using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// A tool result split into the parts that have different audiences: <see cref="Content"/> and
/// <see cref="StructuredContent"/> drive presentation, <see cref="Meta"/> is host/UI metadata, and
/// <see cref="ModelContent"/> is the only part that goes back into the model's context.
/// </summary>
public sealed record ToolCallResult(
    IReadOnlyList<ToolContent> Content,
    JsonElement? StructuredContent,
    JsonElement? Meta,
    bool IsError,
    string ModelContent);

/// <summary>One MCP content block, flattened to what presentation actually needs.</summary>
public sealed record ToolContent(
    ToolContentKind Kind,
    string? Text,
    string? MimeType,
    string? Uri,
    string? Name)
{
    public static ToolContent OfText(string text) => new(ToolContentKind.Text, text, null, null, null);

    internal JsonObject ToModelJson()
    {
        var node = new JsonObject { ["type"] = Kind.ToWireType() };
        if (Text is not null) node["text"] = Text;
        if (MimeType is not null) node["mimeType"] = MimeType;
        if (Uri is not null) node["uri"] = Uri;
        if (Name is not null) node["name"] = Name;
        return node;
    }
}

public enum ToolContentKind
{
    /// <summary>A block type this build does not model; kept so an unknown result still renders.</summary>
    Unknown,
    Text,
    Image,
    Audio,
    ResourceLink,
    Resource,
}

public static class ToolContentKindExtensions
{
    public static string ToWireType(this ToolContentKind kind) => kind switch
    {
        ToolContentKind.Text => "text",
        ToolContentKind.Image => "image",
        ToolContentKind.Audio => "audio",
        ToolContentKind.ResourceLink => "resource_link",
        ToolContentKind.Resource => "resource",
        _ => "unknown",
    };

    public static ToolContentKind FromWireType(string? type) => type switch
    {
        "text" => ToolContentKind.Text,
        "image" => ToolContentKind.Image,
        "audio" => ToolContentKind.Audio,
        "resource_link" => ToolContentKind.ResourceLink,
        "resource" => ToolContentKind.Resource,
        _ => ToolContentKind.Unknown,
    };
}
