namespace AI.Contracts.Settings;

/// <summary>Valid Chat Completions reasoning_effort values; individual models may support fewer.</summary>
public static class ReasoningEffortValues
{
    public static bool IsSupported(string value) => value is
        "none" or "minimal" or "low" or "medium" or "high" or "xhigh" or "max";
}
