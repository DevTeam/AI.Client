namespace AI.Client.Application.Tools;

using System.Text.Json;

/// <summary>
/// Detection for the MCP Apps extension: a tool may point at a <c>ui://</c> resource the host can
/// render instead of a native row.
/// </summary>
/// <remarks>
/// Detection is deliberately separate from rendering, and rendering is not implemented. Running a
/// server-supplied view means executing untrusted markup, which is only safe with the extension's
/// full contract — sandboxed frame, deny-by-default CSP, the AppBridge message channel, and an
/// explicit size lifecycle. `ModelContextProtocol.Core` 2.2.0 carries none of that, so a partial
/// implementation would be a security hole rather than a feature.
///
/// What exists here is the part that is safe and useful now: the declaration is parsed and kept, so
/// a host that gains the runtime later can act on it, and until then <see cref="IsEnabled"/> is
/// false and every tool renders natively. Approval chrome would stay host-native and outside any
/// such frame in any case: what a user confirms must never be drawn by the party being confirmed.
/// </remarks>
public static class ToolUserInterface
{
    /// <summary>
    /// Whether this build can render an MCP App. False, and not configurable: there is no sandbox
    /// to render one into. Call sites read this rather than assuming, so enabling it later is a
    /// single change.
    /// </summary>
    public static bool IsEnabled => false;

    /// <summary>
    /// The <c>ui://</c> resource a tool declares under <c>_meta.ui.resourceUri</c>, or null.
    /// Only the <c>ui:</c> scheme is accepted: the field names a resource to be fetched from the
    /// declaring server, never an arbitrary URL to be loaded from the network.
    /// </summary>
    public static string? ResourceUri(ToolDescriptor tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (tool.Meta is not { ValueKind: JsonValueKind.Object } meta) return null;
        if (!meta.TryGetProperty("ui", out var ui) || ui.ValueKind != JsonValueKind.Object) return null;
        if (!ui.TryGetProperty("resourceUri", out var value) || value.ValueKind != JsonValueKind.String) return null;
        var uri = value.GetString();
        return string.IsNullOrWhiteSpace(uri) || !uri.StartsWith("ui://", StringComparison.Ordinal) ? null : uri;
    }

    /// <summary>Whether a tool declares a renderable view this host would use if it could.</summary>
    public static bool DeclaresApp(ToolDescriptor tool) => ResourceUri(tool) is not null;
}
