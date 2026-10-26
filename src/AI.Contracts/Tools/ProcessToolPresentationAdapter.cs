using System.Globalization;
using System.Text.Json;

namespace AI.Contracts.Tools;

/// <summary>The built-in process runner.</summary>
public sealed class ProcessToolPresentationAdapter : BuiltInToolPresentationAdapter
{
    protected override IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal) { "process_run", "process_run_unrestricted", "process_wait", "process_stop" };

    public override ToolResultPresentation DescribeResult(ToolRef tool, JsonElement? arguments, ToolCallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var structured = result.StructuredContent is { ValueKind: JsonValueKind.Object } value ? value : (JsonElement?)null;
        // These are process outcomes, including partial logs, rather than broken invocations.
        return Text(structured, "status") is "running" or "stopped" or "timed_out"
               || (Number(structured, "exitCode") is not null && Text(structured, "error") is null)
            ? Describe(tool, arguments, structured, result)
            : base.DescribeResult(tool, arguments, result);
    }

    public override ToolCallPresentation DescribeCall(ToolRef tool, JsonElement? arguments)
    {
        if (tool.Name is "process_wait" or "process_stop")
            return new ToolCallPresentation(tool.Name == "process_wait" ? "Wait for command" : "Stop command",
                Argument(arguments, "processId"), tool.Name == "process_wait" ? ToolSafety.ReadOnly : ToolSafety.Destructive);
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
        var limit = Text(structured, "limitExceeded");
        var status = Text(structured, "status");
        var running = status == "running";
        var summary = running ? "Running"
            : status == "stopped" ? "Stopped · verification incomplete"
            : timedOut ? "Timed out · verification incomplete"
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
            summary + (limit is null ? "" : $" · {limit} limit exceeded") + (truncated ? " · output truncated" : ""),
            // A non-zero exit is a real outcome the model will act on, not a host failure, so it
            // stays a warning: the row draws the eye without claiming the call broke.
            SeverityFor(timedOut || status == "stopped" || truncated || limit is not null || (!running && exitCode is not 0)),
            Facts(("Exit code", exitCode?.ToString(CultureInfo.InvariantCulture)),
                ("Process", Text(structured, "processId")),
                ("Stdout log", Text(structured, "stdoutPath")),
                ("Stderr log", Text(structured, "stderrPath")),
                ("Duration", Size(structured, "durationMs") is { } ms ? $"{ms} ms" : null),
                ("Timed out", timedOut ? "yes" : null),
                ("Limit exceeded", limit),
                ("Output truncated", truncated ? "yes" : null),
                // Which OS sandbox confined the run: a denied access is then the boundary, not a fault.
                ("Sandbox", Text(structured, "sandbox"))),
            string.IsNullOrEmpty(body) ? null : body);
    }

    private static string? Shorten(string? command) =>
        command is { Length: > 96 } ? command[..96] + "…" : command;

    private static List<ToolFact> Facts(params (string Label, string? Value)[] facts) =>
        [.. facts.Where(fact => !string.IsNullOrWhiteSpace(fact.Value)).Select(fact => new ToolFact(fact.Label, fact.Value!))];
}
