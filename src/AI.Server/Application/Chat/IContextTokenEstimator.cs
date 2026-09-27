namespace AI.Application.Chat;


/// <summary>Conservatively estimates the model tokens occupied by request payload parts.</summary>
public interface IContextTokenEstimator
{
    long EstimateMessages(IReadOnlyList<ChatCompletionMessage> messages);

    long EstimateTools(IReadOnlyList<ChatToolDefinition> tools);
}
