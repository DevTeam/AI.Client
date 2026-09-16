using System.Globalization;
using System.Text.Json;

namespace AI.Client.Contracts.Tools;

/// <summary>The built-in web fetch tool.</summary>
public sealed class WebToolPresentationAdapter : BuiltInToolPresentationAdapter
{
    protected override IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal) { "fetch" };

    public override ToolCallPresentation DescribeCall(ToolRef tool, JsonElement? arguments) =>
        // The host is the part worth reading at a glance; the full URL stays in the arguments.
        new("Fetch page", Host(Argument(arguments, "url")), ToolSafety.ReadOnly);

    protected override ToolResultPresentation Describe(
        ToolRef tool, JsonElement? arguments, JsonElement? structured, ToolCallResult result)
    {
        var status = Number(structured, "status");
        var truncated = Flag(structured, "truncated");
        return new ToolResultPresentation(
            (status is { } code ? $"HTTP {code}" : "Fetched") + (truncated ? " · truncated" : ""),
            SeverityFor(truncated || status is not (>= 200 and < 300)),
            Facts(("Status", status?.ToString(CultureInfo.InvariantCulture)), ("Content type", Text(structured, "contentType")),
                ("URL", Text(structured, "url")), ("Truncated", truncated ? "yes" : null)),
            Text(structured, "content"));
    }

    private static string? Host(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed.Host : url;

    private static List<ToolFact> Facts(params (string Label, string? Value)[] facts) =>
        [.. facts.Where(fact => !string.IsNullOrWhiteSpace(fact.Value)).Select(fact => new ToolFact(fact.Label, fact.Value!))];
}