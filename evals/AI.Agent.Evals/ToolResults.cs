namespace AI.Agent.Evals;

using System.Text.Json;

public static class ToolResults
{
    /// <summary>Whether an error result is a process that ran and exited non-zero, as process_run reports it.</summary>
    public static bool IsNonZeroExit(string content)
    {
        try
        {
            using var json = JsonDocument.Parse(content);
            return json.RootElement.TryGetProperty("structuredContent", out var result)
                && result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("exitCode", out var code) && code.ValueKind == JsonValueKind.Number && code.GetInt32() != 0
                && !(result.TryGetProperty("timedOut", out var timedOut) && timedOut.ValueKind == JsonValueKind.True)
                && !(result.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
