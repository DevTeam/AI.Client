namespace AI.Client.Application.Tools;

public interface IToolSearchDefinitionEnricher
{
    IReadOnlyList<AgentTool> Enrich(
        IReadOnlyList<AgentTool> selectedTools,
        IReadOnlyList<AgentTool> permittedTools,
        long schemaBudgetTokens);
}
