namespace AI.Application.Tools;

using System.Text.Json;

public sealed class ToolUserInterface : IToolUserInterface
{
    public bool IsEnabled => false;

    public string? ResourceUri(ToolDescriptor tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (tool.Meta is not { ValueKind: JsonValueKind.Object } meta) return null;
        if (!meta.TryGetProperty("ui", out var ui) || ui.ValueKind != JsonValueKind.Object) return null;
        if (!ui.TryGetProperty("resourceUri", out var value) || value.ValueKind != JsonValueKind.String) return null;
        var uri = value.GetString();
        return string.IsNullOrWhiteSpace(uri) || !uri.StartsWith("ui://", StringComparison.Ordinal) ? null : uri;
    }

    public bool DeclaresApp(ToolDescriptor tool) => ResourceUri(tool) is not null;
}
