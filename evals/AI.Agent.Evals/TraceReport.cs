namespace AI.Agent.Evals;

using System.Globalization;
using System.Text;
using System.Text.Json;
using AI.Contracts.Chats;

/// <summary>
/// One attempt as a short timeline — what was asked, each step's words and calls, each result's
/// head — so a person or an agent can see where it went wrong without reading the whole chat.
/// </summary>
public static class TraceReport
{
    public static string Write(string attemptDirectory, int width)
    {
        var result = JsonSerializer.Deserialize<AttemptResult>(File.ReadAllText(Path.Combine(attemptDirectory, "result.json")), Scenario.Json)
            ?? throw new InvalidDataException($"{attemptDirectory} has no result.");
        var text = new StringBuilder();
        text.AppendLine(Invariant($"# {result.Scenario} #{result.Attempt}: {(result.Passed ? "passed" : "FAILED")} ({result.Outcome})"));
        text.AppendLine();
        text.AppendLine(Invariant($"{result.ToolCalls} tool calls, {result.ToolErrors} errors, {result.NonZeroExits} non-zero exits, {result.ModelSteps} steps, {result.Compactions} compactions, {result.InputTokens} in / {result.OutputTokens} out tokens, {result.DurationMs / 1000} s."));
        if (result.Error is { } error) text.AppendLine(Invariant($"Error: {error}"));
        foreach (var check in result.Checks)
            text.AppendLine(Invariant($"- {(check.Passed ? "✓" : "✗")} {check.Name}{(check.Detail is { } detail && !check.Passed ? ": " + Cut(detail, width) : "")}"));
        if (result.WorkspaceChanges.Count > 0) text.AppendLine(Invariant($"Changed: {string.Join(", ", result.WorkspaceChanges)}"));
        text.AppendLine();

        var chatFile = Path.Combine(attemptDirectory, "chat.json");
        if (!File.Exists(chatFile)) return text.ToString();
        var chat = EvalHost.ReadChat(JsonDocument.Parse(File.ReadAllText(chatFile)).RootElement);
        var names = (chat?.Messages ?? []).SelectMany(message => message.ToolCalls ?? []).ToDictionary(call => call.Id, call => call.Name);
        var step = 0;
        foreach (var message in chat?.Messages ?? [])
        {
            var who = message.Sender is null ? "" : " (subtask)";
            switch (message.Role)
            {
                case "User":
                    text.AppendLine(Invariant($"## User{who}"));
                    text.AppendLine(Cut(message.Content, width * 3));
                    break;
                case "Assistant":
                    step++;
                    if (!string.IsNullOrWhiteSpace(message.Content))
                        text.AppendLine(Invariant($"**{step}.{who}** {Cut(message.Content, width * 2)}"));
                    foreach (var call in message.ToolCalls ?? [])
                        text.AppendLine(Invariant($"**{step}.{who}** → `{Short(call.Name)}` {Cut(call.Arguments, width)}"));
                    break;
                case "Tool":
                    var mark = message.ToolResultIsError == true
                        ? ToolResults.IsNonZeroExit(message.Content) ? "exit≠0" : "ERROR"
                        : "ok";
                    var name = message.ToolCallId is { } id && names.TryGetValue(id, out var called) ? Short(called) : "?";
                    text.AppendLine(Invariant($"   ← `{name}` {mark}: {Cut(Payload(message.Content), width)}"));
                    break;
            }
        }

        return text.ToString();
    }

    // A tool result is an MCP envelope around the text the model reads; the text is what matters.
    private static string Payload(string content)
    {
        try
        {
            using var json = JsonDocument.Parse(content);
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("content", out var parts)
                && parts.ValueKind == JsonValueKind.Array)
                return string.Join(" ", parts.EnumerateArray()
                    .Select(part => part.TryGetProperty("text", out var value) ? value.GetString() : $"[{part.GetProperty("type").GetString()}]"));
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException) { }

        return content;
    }

    private static string Short(string name) => name.LastIndexOf("__", StringComparison.Ordinal) is var index and >= 0 ? name[(index + 2)..] : name;

    private static string Cut(string value, int width)
    {
        var line = value.ReplaceLineEndings(" ⏎ ");
        return line.Length <= width ? line : line[..width] + string.Create(CultureInfo.InvariantCulture, $"… (+{line.Length - width})");
    }

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
