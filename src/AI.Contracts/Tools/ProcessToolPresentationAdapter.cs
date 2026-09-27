using System.Globalization;
using System.Text.Json;

namespace AI.Contracts.Tools;

/// <summary>The built-in process runner.</summary>
public sealed class ProcessToolPresentationAdapter : BuiltInToolPresentationAdapter
{
    protected override IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal) { "process_run" };

    public override ToolCallPresentation DescribeCall(ToolRef tool, JsonElement? arguments)
    {
        var executable = Argument(arguments, "executable");
        var parts = ArgumentArray(arguments, "arguments");
        var detail = executable is null
            ? null
            : parts is { } list
                ? (executable + " " + string.Join(' ', list.EnumerateArray()
                    .Where(part => part.ValueKind == JsonValueKind.String)
                    .Select(part => part.GetString()))).Trim()
                : executable;
        return new ToolCallPresentation("Run command", Shorten(detail), ToolSafety.Destructive);
    }

    protected override ToolResultPresentation Describe(
        ToolRef tool, JsonElement? arguments, JsonElement? structured, ToolCallResult result)
    {
        var exitCode = Number(structured, "exitCode");
        var timedOut = Flag(structured, "timedOut");
        var truncated = Flag(structured, "truncated");
        var summary = timedOut ? "Timed out"
            : exitCode is { } code ? (code == 0 ? "Exit 0" : $"Exit {code}")
            : "Finished";
        var stdout = Text(structured, "stdout");
        var stderr = Text(structured, "stderr");
        var body = string.Join("\n", new[]
        {
            string.IsNullOrEmpty(stdout) ? null : stdout,
            string.IsNullOrEmpty(stderr) ? null : "stderr:\n" + stderr,
        }.Where(part => part is not null));

        return new ToolResultPresentation(
            summary + (truncated ? " · output truncated" : ""),
            // A non-zero exit is a real outcome the model will act on, not a host failure, so it
            // stays a warning: the row draws the eye without claiming the call broke.
            SeverityFor(timedOut || truncated || exitCode is not 0),
            Facts(("Exit code", exitCode?.ToString(CultureInfo.InvariantCulture)),
                ("Duration", Size(structured, "durationMs") is { } ms ? $"{ms} ms" : null),
                ("Timed out", timedOut ? "yes" : null),
                ("Output truncated", truncated ? "yes" : null)),
            string.IsNullOrEmpty(body) ? null : body);
    }

    private static string? Shorten(string? command) =>
        command is { Length: > 96 } ? command[..96] + "…" : command;

    private static List<ToolFact> Facts(params (string Label, string? Value)[] facts) =>
        [.. facts.Where(fact => !string.IsNullOrWhiteSpace(fact.Value)).Select(fact => new ToolFact(fact.Label, fact.Value!))];
}