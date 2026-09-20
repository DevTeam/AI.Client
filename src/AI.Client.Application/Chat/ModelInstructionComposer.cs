namespace AI.Client.Application.Chat;

using Contracts.Chat;
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
        foreach (var instruction in registry.List(run)
                     .OrderByDescending(item => item.Priority)
                     .ThenBy(item => item.Key, StringComparer.Ordinal))
        {
            var message = new ChatCompletionMessage("system", instruction.Content);
            var cost = estimator.EstimateMessages([message]);
            if (selected.Count > 0 && tokens + cost > InstructionBudgetTokens) continue;
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
