namespace AI.Contracts.Tools;

using System.Text.Json;

/// <summary>
/// What the person said back to an <c>ask_user</c> question, read on the server from the call and
/// its result so the transcript can show it as their reply whether or not either body was sent.
/// </summary>
/// <param name="Outcome">The result's <c>outcome</c>: <c>answered</c>, <c>dismissed</c>, <c>declined</c> or <c>expired</c>.</param>
/// <param name="Answers">One entry per question, in the order the questions were asked.</param>
public sealed record AskUserAnswer(string Outcome, IReadOnlyList<AskUserAnswerItem> Answers);

/// <param name="Label">The question's short chip, when it has one.</param>
/// <param name="Question">The question as it was put.</param>
/// <param name="Answer">The chosen options, picked values and free text as one answer; empty when left open.</param>
/// <param name="IsPath">Whether the answer is only picked paths or values, which read best in monospace.</param>
public sealed record AskUserAnswerItem(string? Label, string Question, string Answer, bool IsPath);

public static class AskUserAnswers
{
    /// <summary>The question tool's name on the application server, without the server prefix.</summary>
    public const string ToolName = "ask_user";

    public static bool IsAskUserCall(string callName) =>
        ToolRef.Parse(callName) is { IsApp: true, Name: ToolName };

    /// <summary>
    /// The person's reply, or null where there was none to show: an error, a question that could not
    /// be asked, a run interrupted before anyone saw it, or history too old or damaged to read.
    /// </summary>
    public static AskUserAnswer? TryRead(string? arguments, string? content)
    {
        if (string.IsNullOrWhiteSpace(arguments) || string.IsNullOrWhiteSpace(content)) return null;
        try
        {
            using var call = JsonDocument.Parse(arguments);
            using var result = JsonDocument.Parse(content);
            var root = result.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True
                || !root.TryGetProperty("structuredContent", out var structured)
                || structured.ValueKind != JsonValueKind.Object
                || call.RootElement.ValueKind != JsonValueKind.Object
                || !call.RootElement.TryGetProperty("questions", out var questions)
                || questions.ValueKind != JsonValueKind.Array) return null;

            var outcome = Text(structured, "outcome");
            // Interrupted and invalid questions never reached the person: nothing of theirs to show.
            if (outcome is not ("answered" or "dismissed" or "declined" or "expired")) return null;

            var answers = structured.TryGetProperty("answers", out var found) && found.ValueKind == JsonValueKind.Array
                ? found.EnumerateArray().ToArray()
                : [];
            var items = new List<AskUserAnswerItem>();
            foreach (var question in questions.EnumerateArray())
            {
                var id = Text(question, "id");
                var answer = answers.FirstOrDefault(item => id is not null && Text(item, "id") == id);
                var chosen = answer.ValueKind == JsonValueKind.Object ? Chosen(answer) : (Text: string.Empty, IsPath: false);
                items.Add(new AskUserAnswerItem(Text(question, "label"), Text(question, "text") ?? Text(question, "label") ?? "Question",
                    chosen.Text, chosen.IsPath));
            }
            return items.Count == 0 ? null : new AskUserAnswer(outcome, items);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Selected labels and free text read as one answer, because that is how it was given.</summary>
    internal static (string Text, bool IsPath) Chosen(JsonElement answer)
    {
        var parts = new List<string>();
        if (answer.TryGetProperty("selected", out var selected) && selected.ValueKind == JsonValueKind.Array)
            parts.AddRange(selected.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .Where(item => item.Length > 0));
        if (Text(answer, "other") is { Length: > 0 } other) parts.Add(other);
        var worded = parts.Count;
        // A recurrence reads in words; its JSON is for the model.
        var described = answer.TryGetProperty("valueDescriptions", out var words) && words.ValueKind == JsonValueKind.Array;
        var labels = parts.ToArray();
        foreach (var property in new[] { "paths", described ? "valueDescriptions" : "values" })
            if (answer.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array)
                parts.AddRange(values.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!).Where(item => item.Length > 0)
                    // An option's value that its label already spells out says it a second time.
                    .Where(item => !labels.Any(label => label.Contains(item, StringComparison.Ordinal))));
        return (string.Join(", ", parts), worded == 0 && parts.Count > 0 && !described);
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var found)
                                                  && found.ValueKind == JsonValueKind.String
            ? found.GetString()
            : null;
}
