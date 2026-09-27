namespace AI.Application.Chat;

using Tools;

public sealed record ModelInstructionComposition(
    IReadOnlyList<ChatCompletionMessage> Messages,
    IReadOnlyList<string> Keys,
    long EstimatedTokens);

/// <summary>
/// Builds the model-only system preamble. It is the sole boundary at which hidden application
/// instructions become provider messages, keeping persistence and UI code unaware of their text.
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
