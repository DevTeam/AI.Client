namespace AI.Application.Tools;

public interface IToolSelectionPriorityPolicy
{
    bool IsRequired(AgentTool tool);

    bool IsPreferred(AgentTool tool);
}
