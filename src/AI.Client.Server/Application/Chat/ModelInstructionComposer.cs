namespace AI.Client.Application.Chat;

using Tools;

public sealed class ModelInstructionComposer(
    IModelInstructionRegistry registry,
    IContextTokenEstimator estimator) : IModelInstructionComposer
{
    private const long InstructionBudgetTokens = 2_048;

    public ModelInstructionComposition Compose(ToolRunContext run, IReadOnlyList<ChatCompletionMessage> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var selected = new List<ModelInstruction>();
        long tokens = 0;
        long runTokens = 0;
        foreach (var instruction in registry.List(run)
                     .OrderByDescending(item => item.Placement)
                     .ThenByDescending(item => item.Priority)
                     .ThenBy(item => item.Key, StringComparer.Ordinal))
        {
            var message = new ChatCompletionMessage("system", instruction.Content);
            var cost = estimator.EstimateMessages([message]);
            // A standing layer was already fitted to its own budget when it was built; only run
            // instructions compete for this one.
            if (instruction.Placement == ModelInstructionPlacement.Run)
            {
                if (runTokens > 0 && runTokens + cost > InstructionBudgetTokens) continue;
                runTokens = Add(runTokens, cost);
            }
            selected.Add(instruction);
            tokens = Add(tokens, cost);
        }

        if (selected.Count == 0)
            return new ModelInstructionComposition(context, [], 0);

        var messages = selected.Select(item => new ChatCompletionMessage("system", item.Content))
            .Concat(context).ToArray();
        return new ModelInstructionComposition(messages, selected.Select(item => item.Key).ToArray(), tokens);
    }

    public void Acknowledge(ToolRunContext run, ModelInstructionComposition composition) =>
        registry.Acknowledge(run, composition.Keys);

    private static long Add(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;
}
