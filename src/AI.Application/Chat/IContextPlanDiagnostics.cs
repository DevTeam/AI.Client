namespace AI.Application.Chat;

/// <summary>Publishes context measurements without exposing message or tool contents.</summary>
public interface IContextPlanDiagnostics
{
    void Record(string model, ContextPlan plan, int messageCount, int toolCount);

    void RecordToolSelection(string model, ToolSelection selection);
}
