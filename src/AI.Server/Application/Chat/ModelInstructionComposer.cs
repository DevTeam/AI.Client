namespace AI.Application.Chat;

using Tools;
using Contracts.Settings;

public sealed class ModelInstructionComposer(
    IModelInstructionRegistry registry,
    IContextTokenEstimator estimator, IAdaptiveContextPolicy policy) : IModelInstructionComposer
{
    /// <summary>
    /// Opens the trailing note. It is added to the last message rather than sent as one of its own:
    /// several chat templates reject a system message anywhere but first, and others two messages
    /// of one role in a row. The tag keeps it apart from what the user or a tool wrote.
    /// </summary>
    public const string TrailingOpen = "<application-guidance>\nFrom the application for this step, not from the user.\n";

    public const string TrailingClose = "\n</application-guidance>";

    public ModelInstructionComposition Compose(ToolRunContext run, IReadOnlyList<ChatCompletionMessage> context,
        ConnectionSettings? connection = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var selected = policy.SelectInstructions(registry.List(run), connection);
        var tokens = estimator.EstimateMessages(selected.Select(item => new ChatCompletionMessage("system", item.Content)).ToArray());

        if (selected.Count == 0)
            return new ModelInstructionComposition(context, [], 0);

        var leading = selected.Where(item => item.Placement != ModelInstructionPlacement.Trailing).ToArray();
        var trailing = selected.Where(item => item.Placement == ModelInstructionPlacement.Trailing).ToArray();
        var messages = leading.Select(item => new ChatCompletionMessage("system", item.Content))
            .Concat(context).ToArray();
        var note = trailing.Length == 0
            ? null
            : TrailingOpen + string.Join("\n\n", trailing.Select(item => item.Content)) + TrailingClose;
        return new ModelInstructionComposition(messages, leading.Concat(trailing).Select(item => item.Key).ToArray(),
            tokens, note);
    }

    public void Acknowledge(ToolRunContext run, ModelInstructionComposition composition) =>
        registry.Acknowledge(run, composition.Keys);
}
