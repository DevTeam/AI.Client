namespace AI.Application.Chat;


/// <summary>Conservatively estimates the model tokens occupied by request payload parts.</summary>
public interface IContextTokenEstimator
{
    /// <summary>A model-bound view; unknown encodings keep the conservative estimator.</summary>
    IContextTokenEstimator ForModel(string? model) => this;

    long EstimateMessages(IReadOnlyList<ChatCompletionMessage> messages);

    long EstimateTools(IReadOnlyList<ChatToolDefinition> tools);
}
