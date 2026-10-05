using System.Globalization;
using System.Text.Json;

namespace AI.Contracts.Tools;

/// <summary>
/// The optional C# scripting server. A script runs inside the server's own process and is not
/// sandboxed — it reaches whatever that process can, exactly like <c>process_run</c> — so a call is
/// marked as dangerous as a command. The result is the script's own: what it returned, what it
/// declared, what it printed and what the compiler said about it, never a restatement of the code
/// that was sent.
/// </summary>
public sealed class CSharpToolPresentationAdapter : BuiltInToolPresentationAdapter
{
    /// <summary>How many compiler messages the facts list carries before the rest stay in the raw result.</summary>
    private const int DiagnosticLimit = 5;

    private const int DetailLimit = 72;

    protected override IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal) { "cs_run" };

    protected override string Prefix => ToolRef.CSharpPrefix;

    public override ToolCallPresentation DescribeCall(ToolRef tool, JsonElement? arguments)
    {
        ArgumentNullException.ThrowIfNull(tool);
        // A script commonly opens with `using` directives and blank lines, so the first line that
        // says anything is a better row than whichever one happens to come first; the whole code
        // stays in the expanded arguments.
        return new ToolCallPresentation("Run C#", FirstCodeLine(Argument(arguments, "code")), ToolSafety.Destructive);
    }

    /// <summary>
    /// The scripting server reports a run cut short by its deadline through both <c>timedOut</c> and
    /// its <c>error</c> message, and the shared path would read that message as a hard failure. A
    /// timeout is the same outcome <c>process_run</c> reports: it really happened, it is worth
    /// noticing, and the call did not break. Everything else — an empty script, a compile error —
    /// keeps the shared error handling.
    /// </summary>
    public override ToolResultPresentation DescribeResult(
        ToolRef tool, JsonElement? arguments, ToolCallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var structured = result.StructuredContent is { ValueKind: JsonValueKind.Object } value
            ? value
            : (JsonElement?)null;
        return Flag(structured, "timedOut")
            ? Describe(tool, arguments, structured, result)
            : base.DescribeResult(tool, arguments, result);
    }

    protected override ToolResultPresentation Describe(
        ToolRef tool, JsonElement? arguments, JsonElement? structured, ToolCallResult result)
    {
        var timedOut = Flag(structured, "timedOut");
        var truncated = Flag(structured, "truncated");
        var returnValue = Text(structured, "returnValue");
        var variables = Count(structured, "variables") ?? 0;
        var facts = Facts(
            ("Return type", Text(structured, "returnType")),
            ("Variables", variables > 0 ? variables.ToString(CultureInfo.InvariantCulture) : null),
            ("Warnings", Warnings(structured, out var warnings) > 0
                ? warnings.ToString(CultureInfo.InvariantCulture)
                : null),
            ("Duration", Size(structured, "durationMs") is { } milliseconds ? $"{milliseconds} ms" : null),
            ("Timed out", timedOut ? "yes" : null),
            ("Output truncated", truncated ? "yes" : null));
        facts.AddRange(DiagnosticFacts(structured));

        var summary = timedOut ? "Timed out"
            : returnValue is { Length: > 0 } value ? $"Returned {Clip(value, 60)}"
            : "Completed";

        return new ToolResultPresentation(
            summary + (truncated ? " · output truncated" : string.Empty),
            // A compiler warning or a cut-short capture is an outcome worth noticing, not a failure:
            // the script did run and this is what it produced.
            SeverityFor(timedOut || truncated || warnings > 0),
            facts,
            Body(Text(structured, "stdout"), Text(structured, "stderr")));
    }

    /// <summary>
    /// The compiler's messages, which are what a script that compiled with doubts has to say beyond
    /// its output. Only the first few become rows of their own; the count covers all of them.
    /// </summary>
    private static List<ToolFact> DiagnosticFacts(JsonElement? structured)
    {
        var facts = new List<ToolFact>();
        if (structured is not { ValueKind: JsonValueKind.Object } value
            || !value.TryGetProperty("diagnostics", out var found)
            || found.ValueKind != JsonValueKind.Array)
            return facts;

        foreach (var diagnostic in found.EnumerateArray())
        {
            if (facts.Count == DiagnosticLimit) break;
            var detail = $"{Text(diagnostic, "severity") ?? "Message"} {Text(diagnostic, "id")}: {Text(diagnostic, "message")}"
                         + (Number(diagnostic, "line") is { } line and > 0 ? $" (line {line})" : string.Empty);
            facts.Add(new ToolFact($"Diagnostic {facts.Count + 1}", Clip(detail.Trim(), 160)));
        }

        return facts;
    }

    /// <summary>How many of the compiler's messages were warnings; errors make the call fail instead.</summary>
    private static int Warnings(JsonElement? structured, out int warnings)
    {
        warnings = 0;
        if (structured is not { ValueKind: JsonValueKind.Object } value
            || !value.TryGetProperty("diagnostics", out var found)
            || found.ValueKind != JsonValueKind.Array)
            return warnings;

        foreach (var diagnostic in found.EnumerateArray())
            if (Text(diagnostic, "severity") == "Warning")
                warnings++;
        return warnings;
    }

    /// <summary>The two streams the script wrote, labelled the same way a command's output is.</summary>
    private static string? Body(string? stdout, string? stderr)
    {
        var text = string.Join("\n", new[]
        {
            string.IsNullOrEmpty(stdout) ? null : stdout,
            string.IsNullOrEmpty(stderr) ? null : "stderr:\n" + stderr,
        }.Where(part => part is not null));
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// The first line of the script that says what it does. A script normally opens with blank lines
    /// and <c>using</c> directives, which every script has and no two differ in, so the row is worth
    /// more below them; a script that is nothing but directives still gets its first one.
    /// </summary>
    private static string? FirstCodeLine(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        string? first = null;
        foreach (var line in code.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.Trim() is not { Length: > 0 } trimmed) continue;
            first ??= trimmed;
            if (IsUsingDirective(trimmed)) continue;
            return Clip(trimmed, DetailLimit);
        }

        return first is null ? null : Clip(first, DetailLimit);
    }

    /// <summary>A <c>using</c> directive, as opposed to a <c>using</c> statement or declaration.</summary>
    private static bool IsUsingDirective(string line) =>
        line.StartsWith("using ", StringComparison.Ordinal)
        && line.EndsWith(';')
        && !line.StartsWith("using var ", StringComparison.Ordinal);

    private static string Clip(string text, int length) =>
        text.Length <= length ? text : text[..(length - 1)] + "…";

    private static List<ToolFact> Facts(params (string Label, string? Value)[] facts) =>
        [.. facts.Where(fact => !string.IsNullOrWhiteSpace(fact.Value)).Select(fact => new ToolFact(fact.Label, fact.Value!))];
}
