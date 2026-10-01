namespace AI.Application.Chat;

using Tools;

/// <param name="Messages">The leading instructions followed by the conversation.</param>
/// <param name="Trailing">
/// Step-specific guidance that goes after the conversation, ahead of nothing the provider could
/// have cached. Empty when there is none.
/// </param>
public sealed record ModelInstructionComposition(
    IReadOnlyList<ChatCompletionMessage> Messages,
    IReadOnlyList<string> Keys,
    long EstimatedTokens,
    IReadOnlyList<ChatCompletionMessage>? Trailing = null)
{
    public IReadOnlyList<ChatCompletionMessage> TrailingMessages => Trailing ?? [];
}

/// <summary>
/// Builds the model-only instructions. It is the sole boundary at which hidden application
/// instructions become provider messages, keeping persistence and UI code unaware of their text.
/// Providers cache a request by its prefix, so what is stable for the run leads it and what changes
/// from step to step follows the conversation.
/// </summary>
public interface IModelInstructionComposer
{
    ModelInstructionComposition Compose(ToolRunContext run, IReadOnlyList<ChatCompletionMessage> context);
    void Acknowledge(ToolRunContext run, ModelInstructionComposition composition);
}

/// <summary>Records only instruction identities and size, never their content.</summary>
public interface IModelInstructionDiagnostics
{
    void RecordInstructions(string model, IReadOnlyList<string> keys, long estimatedTokens);

    void RecordEmptyResponse(string model, int attempt, string? finishReason, int chunkCount,
        bool completionRequired, bool completionToolForced);
}
