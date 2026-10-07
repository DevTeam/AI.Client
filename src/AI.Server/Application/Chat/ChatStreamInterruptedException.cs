namespace AI.Application.Chat;

/// <summary>
/// A response stream that started and then broke off before its finish: it fell silent, or ended
/// in the middle of a tool call. Nothing the model said in it has been acted on, so the step can be
/// asked for again; see <see cref="Tools.ChatAgent"/>.
/// </summary>
public sealed class ChatStreamInterruptedException(string message) : InvalidOperationException(message);
