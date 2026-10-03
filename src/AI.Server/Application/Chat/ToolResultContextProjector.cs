namespace AI.Application.Chat;

using System.Text;
using System.Text.Json;

/// <summary>Bounds model-facing results while retaining structured outcomes and diagnostic lines.</summary>
public interface IToolResultContextProjector
{
    string Project(string content, string toolName, int headCharacters, int tailCharacters);
}

public sealed class ToolResultContextProjector : IToolResultContextProjector
{
    private const string Marker = "[Tool result compacted for model context.";
    private readonly HashSet<string> _factNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "error", "errors", "exitCode", "timedOut", "status", "success", "failed", "applied", "dryRun",
        "path", "filePath", "source", "destination", "resource", "uri", "url", "id", "projectId", "chatId",
        "branchId", "messageId", "revision", "nextCursor", "truncated", "created", "deleted", "returned", "total"
    };

    public string Project(string content, string toolName, int headCharacters, int tailCharacters)
    {
        var allowance = Math.Max(0, headCharacters) + Math.Max(0, tailCharacters);
        if (content.Length <= allowance || content.StartsWith(Marker, StringComparison.Ordinal)) return content;
        var facts = new StringBuilder();
        var factLimit = allowance / 2;
        var diagnosticSources = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array || root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Any(property =>
                    property.Name is "stdout" or "stderr" or "path" or "resource" or "operation" or "files" or "text" or "error"))
            {
                var remaining = 2_048;
                Collect(root, facts, factLimit, diagnosticSources, 0, ref remaining);
            }
        }
        catch (JsonException)
        {
            // Unknown and non-JSON results retain excerpts and diagnostic lines.
        }
        if (diagnosticSources.Count == 0) diagnosticSources.Add(content);
        foreach (var source in diagnosticSources)
        {
            using var reader = new StringReader(source);
            while (facts.Length < factLimit && reader.ReadLine() is { } line)
                if (Diagnostic(line)) Add(facts, "diagnostic", line, factLimit);
        }

        var remainingExcerpt = Math.Max(0, allowance - facts.Length);
        var head = Math.Min(content.Length, remainingExcerpt * 3 / 4);
        var tail = Math.Min(content.Length - head, remainingExcerpt - head);
        return $"{Marker} Tool: {toolName}. Original characters: {content.Length}. "
            + "Re-run the tool or read the retained path/resource again for full details.]\n"
            + (facts.Length > 0 ? "Retained outcomes and diagnostics:\n" + facts : string.Empty)
            + "Beginning:\n" + content[..head] + "\n[...omitted...]\nEnd:\n" + content[^tail..];
    }

    private void Collect(JsonElement element, StringBuilder facts, int limit, List<string> diagnostics,
        int depth, ref int remaining)
    {
        if (depth > 4 || remaining-- <= 0 || facts.Length >= limit) return;
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (remaining <= 0 || facts.Length >= limit) break;
                Collect(item, facts, limit, diagnostics, depth + 1, ref remaining);
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            // Errors and status precede large lists of paths or identifiers.
            foreach (var property in element.EnumerateObject().OrderByDescending(property =>
                         property.Name is "error" or "errors" or "exitCode" or "status" or "timedOut"))
            {
                if (remaining-- <= 0 || facts.Length >= limit) break;
                if (_factNames.Contains(property.Name) && property.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                    Add(facts, property.Name, property.Value.ToString(), limit);
                if (property.Name is "stdout" or "stderr" or "text" && property.Value.ValueKind == JsonValueKind.String)
                    diagnostics.Add(property.Value.GetString()!);
                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    Collect(property.Value, facts, limit, diagnostics, depth + 1, ref remaining);
            }
        }
    }

    private static bool Diagnostic(string line) => line.Contains("error", StringComparison.OrdinalIgnoreCase)
        || line.Contains("fail", StringComparison.OrdinalIgnoreCase)
        || line.Contains("exception", StringComparison.OrdinalIgnoreCase)
        || line.Contains("warning", StringComparison.OrdinalIgnoreCase);

    private static void Add(StringBuilder facts, string name, string value, int limit)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var available = limit - facts.Length - name.Length - 4;
        if (available <= 0) return;
        var size = Math.Min(320, available);
        facts.Append(name).Append(": ").Append(value.AsSpan(0, Math.Min(value.Length, size))).Append('\n');
    }
}
