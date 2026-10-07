namespace AI.Contracts.Tools;

using System.Text.Json;

/// <summary>
/// Describes a question put to the person. The row has to read as a decision rather than as a call:
/// what was chosen is the only part worth a collapsed line, because the question itself was already
/// on screen when it mattered and the answer is what the rest of the run was built on.
/// </summary>
public sealed class AskUserPresentationAdapter : BuiltInToolPresentationAdapter
{
    protected override IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal) { "ask_user" };

    protected override string Prefix => ToolRef.AppPrefix;

    public override ToolCallPresentation DescribeCall(ToolRef tool, JsonElement? arguments)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var questions = ArgumentArray(arguments, "questions");
        var count = questions?.GetArrayLength() ?? 0;
        var first = count == 0 ? null : Text(questions!.Value[0], "text");
        return new ToolCallPresentation(
            count > 1 ? $"Asked {count} questions" : "Asked a question",
            Clip(first, 80),
            // Asking changes nothing. The decision may, but that is the caller's next move.
            ToolSafety.ReadOnly);
    }

    protected override ToolResultPresentation Describe(
        ToolRef tool, JsonElement? arguments, JsonElement? structured, ToolCallResult result)
    {
        var outcome = Text(structured, "outcome") ?? "interrupted";
        var questions = ArgumentArray(arguments, "questions");
        var answers = structured is { } value && value.TryGetProperty("answers", out var found)
                                              && found.ValueKind == JsonValueKind.Array
            ? found.EnumerateArray().ToArray()
            : [];

        var facts = new List<ToolFact>();
        foreach (var answer in answers)
        {
            var id = Text(answer, "id");
            var question = Question(questions, id);
            var chosen = Chosen(answer);
            if (chosen is { Length: > 0 })
                facts.Add(new ToolFact(Clip(Label(question) ?? id, 40) ?? "Answer", chosen));
        }

        // Nothing chosen is a real answer with a real consequence — the model decided by itself
        // from here on — so it is a warning the person can find later, not a silent Ok row.
        if (facts.Count == 0)
            return new ToolResultPresentation(
                outcome switch
                {
                    "expired" => "No answer in time; the model decided",
                    "interrupted" => "Nobody could be asked; the model decided",
                    "invalid" => Text(structured, "error") ?? "The question could not be asked",
                    "declined" => "Declined by the user; the model stopped",
                    _ => "Left to the model"
                },
                outcome == "invalid" ? ToolResultSeverity.Error : ToolResultSeverity.Warning,
                [],
                Text(structured, "guidance"));

        return new ToolResultPresentation(
            facts.Count == 1 ? $"{facts[0].Label}: {facts[0].Value}" : Plural(facts.Count, "answer", "answers"),
            // Answered in part is still answered: the questions left open are named in the guidance
            // the model was given, and the person meant to leave them open.
            ToolResultSeverity.Ok,
            facts,
            Text(structured, "guidance"));
    }

    private static JsonElement? Question(JsonElement? questions, string? id)
    {
        if (questions is not { ValueKind: JsonValueKind.Array } list || string.IsNullOrEmpty(id)) return null;
        foreach (var question in list.EnumerateArray())
            if (Text(question, "id") == id)
                return question;
        return null;
    }

    private static string? Label(JsonElement? question) => Text(question, "label") ?? Text(question, "text");

    /// <summary>Selected labels and free text read as one answer, because that is how it was given.</summary>
    private static string Chosen(JsonElement answer)
    {
        var parts = new List<string>();
        if (answer.TryGetProperty("selected", out var selected) && selected.ValueKind == JsonValueKind.Array)
            parts.AddRange(selected.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .Where(item => item.Length > 0));
        if (Text(answer, "other") is { Length: > 0 } other) parts.Add(other);
        // A recurrence reads in words; its JSON is for the model.
        var described = answer.TryGetProperty("valueDescriptions", out var words) && words.ValueKind == JsonValueKind.Array;
        foreach (var property in new[] { "paths", described ? "valueDescriptions" : "values" })
            if (answer.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array)
                parts.AddRange(values.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!).Where(item => item.Length > 0));
        return string.Join(", ", parts);
    }

    private static string? Clip(string? text, int length) =>
        text is not { Length: > 0 } ? null : text.Length <= length ? text : text[..(length - 1)] + "…";
}
