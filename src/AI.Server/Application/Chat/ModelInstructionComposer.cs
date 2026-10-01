namespace AI.Application.Chat;

using Tools;

public sealed class ModelInstructionComposer(
    IModelInstructionRegistry registry,
    IContextTokenEstimator estimator) : IModelInstructionComposer
{
    private const long InstructionBudgetTokens = 2_048;

    /// <summary>
    /// Opens the trailing note. It is sent with the user role: several chat templates reject a
    /// system message anywhere but first, and the note must not read as something the user wrote.
    /// </summary>
    public const string TrailingPrefix = "[Application guidance for this step. It comes from the application, not from the user.]\n\n";

    public ModelInstructionComposition Compose(ToolRunContext run, IReadOnlyList<ChatCompletionMessage> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var selected = new List<ModelInstruction>();
        long tokens = 0;
        long runTokens = 0;
        foreach (var instruction in registry.List(run)
                     .OrderByDescending(item => Position(item))
                     .ThenByDescending(item => item.Priority)
                     .ThenBy(item => item.Key, StringComparer.Ordinal))
        {
            var message = new ChatCompletionMessage("system", instruction.Content);
            var cost = estimator.EstimateMessages([message]);
            // A standing layer was already fitted to its own budget when it was built; only run
            // instructions compete for this one.
            if (instruction.Placement != ModelInstructionPlacement.Standing)
            {
                if (runTokens > 0 && runTokens + cost > InstructionBudgetTokens) continue;
                runTokens = Add(runTokens, cost);
            }
            selected.Add(instruction);
            tokens = Add(tokens, cost);
        }

        if (selected.Count == 0)
            return new ModelInstructionComposition(context, [], 0);

        var leading = selected.Where(item => Position(item) != ModelInstructionPlacement.Trailing).ToArray();
        var trailing = selected.Where(item => Position(item) == ModelInstructionPlacement.Trailing).ToArray();
        var messages = leading.Select(item => new ChatCompletionMessage("system", item.Content))
            .Concat(context).ToArray();
        ChatCompletionMessage[] note = trailing.Length == 0
            ? []
            : [new ChatCompletionMessage("user", TrailingPrefix + string.Join("\n\n", trailing.Select(item => item.Content)))];
        return new ModelInstructionComposition(messages, leading.Concat(trailing).Select(item => item.Key).ToArray(),
            tokens, note);
    }

    public void Acknowledge(ToolRunContext run, ModelInstructionComposition composition) =>
        registry.Acknowledge(run, composition.Keys);

    /// <summary>
    /// An instruction meant for one request, or until the model answers it, changes the request
    /// it leaves; leading the request, every such change would cost the cached conversation behind it.
    /// </summary>
    private static ModelInstructionPlacement Position(ModelInstruction instruction) =>
        instruction.Placement == ModelInstructionPlacement.Standing ? ModelInstructionPlacement.Standing
        : instruction.Lifetime != ModelInstructionLifetime.Run ? ModelInstructionPlacement.Trailing
        : instruction.Placement;

    private static long Add(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;
}
