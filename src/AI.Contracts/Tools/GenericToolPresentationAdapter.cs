namespace AI.Contracts.Tools;

using System.Text.Json;

/// <summary>
/// The floor every tool lands on, including ones this build has never seen. It claims nothing the
/// protocol did not say: the label comes from the tool's own title or name, the detail from a
/// single scalar argument when there is an obvious one, and the summary from the result's own
/// content.
/// </summary>
public sealed class GenericToolPresentationAdapter : IToolPresentationAdapter
{
    /// <summary>Argument names worth putting on a collapsed row, in order of preference.</summary>
    private static readonly string[] DetailProperties =
        ["path", "paths", "url", "source", "query", "pattern", "executable", "command", "name"];

    private const int SummaryLimit = 120;
    private const int DetailLimit = 72;

    public bool CanHandle(ToolRef tool) => true;

    public ToolCallPresentation DescribeCall(ToolRef tool, JsonElement? arguments)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return new ToolCallPresentation(tool.FallbackLabel, Detail(arguments), tool.Hints);
    }

    public ToolResultPresentation DescribeResult(ToolRef tool, JsonElement? arguments, ToolCallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var text = FirstText(result);
        if (result.IsError)
            return new ToolResultPresentation(Summary(text) ?? "Failed", ToolResultSeverity.Error, Facts(result), text);

        // With no schema knowledge, the only honest summary is the result's own prose — or, when
        // there is none, the fact that it completed. The scalar facts carry the specifics.
        return new ToolResultPresentation(Summary(text) ?? "Done", ToolResultSeverity.Ok, Facts(result), text);
    }

    /// <summary>Picks one short, recognizable argument for the collapsed row.</summary>
    private static string? Detail(JsonElement? arguments)
    {
        if (arguments is not { ValueKind: JsonValueKind.Object } input) return null;
        foreach (var property in DetailProperties)
        {
            if (!input.TryGetProperty(property, out var value)) continue;
            switch (value.ValueKind)
            {
                case JsonValueKind.String when value.GetString() is { Length: > 0 } text:
                    return ShortenTail(text);
                case JsonValueKind.Array:
                {
                    var count = value.GetArrayLength();
                    if (count == 1 && value[0].ValueKind == JsonValueKind.String) return ShortenTail(value[0].GetString());
                    if (count > 0) return $"{count} items";
                    break;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Lifts the scalar fields of a structured result into label/value rows. Only scalars: nested
    /// objects and arrays belong in the raw body, not in a summary list that has to stay readable.
    /// </summary>
    private static List<ToolFact> Facts(ToolCallResult result)
    {
        if (result.StructuredContent is not { ValueKind: JsonValueKind.Object } structured) return [];
        var facts = new List<ToolFact>();
        foreach (var property in structured.EnumerateObject())
        {
            if (facts.Count == 8) break;
            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value.GetRawText(),
                _ => null,
            };
            if (string.IsNullOrWhiteSpace(value)) continue;
            facts.Add(new ToolFact(Humanize(property.Name), Shorten(value)!));
        }
        return facts;
    }

    /// <summary>
    /// A summary only when the result's text is meant to be read. A server using structured
    /// content commonly repeats that JSON verbatim as its text block, and a serialized object is
    /// noise on a one-line row — the facts list says the same thing legibly.
    /// </summary>
    private static string? Summary(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[') ? null : Shorten(text);
    }

    private static string? FirstText(ToolCallResult result)
    {
        foreach (var block in result.Content)
            if (block.Kind == ToolContentKind.Text && !string.IsNullOrWhiteSpace(block.Text))
                return block.Text;
        return null;
    }

    /// <summary>
    /// Shortens from the front. A detail is usually a path or URL, whose end — the file name, the
    /// last path segment — is the part that identifies it; cutting the tail would leave every row
    /// in a directory looking identical.
    /// </summary>
    private static string? ShortenTail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var line = value.ReplaceLineEndings(" ").Trim();
        return line.Length <= DetailLimit ? line : "…" + line[^DetailLimit..];
    }

    /// <summary>Collapses a value to one line short enough for a compact row.</summary>
    private static string? Shorten(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var line = value.ReplaceLineEndings(" ").Trim();
        return line.Length <= SummaryLimit ? line : line[..SummaryLimit] + "…";
    }

    /// <summary><c>exitCode</c> becomes "Exit code".</summary>
    private static string Humanize(string property)
    {
        if (property.Length == 0) return property;
        var text = new System.Text.StringBuilder(property.Length + 8);
        text.Append(char.ToUpperInvariant(property[0]));
        foreach (var character in property.AsSpan(1))
        {
            if (char.IsUpper(character)) text.Append(' ').Append(char.ToLowerInvariant(character));
            else text.Append(character);
        }
        return text.ToString();
    }
}
