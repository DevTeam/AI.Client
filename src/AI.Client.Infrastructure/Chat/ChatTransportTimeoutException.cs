namespace AI.Client.Infrastructure.Chat;

public sealed class ChatResponseHeadersTimeoutException(TimeSpan timeout, Exception innerException)
    : TimeoutException($"The AI endpoint did not return response headers within {timeout}.", innerException);

public sealed class ChatFirstTokenTimeoutException(TimeSpan timeout, Exception innerException)
    : TimeoutException($"The AI endpoint did not return the first token within {timeout}.", innerException);

public sealed class ChatRetryDeadlineExceededException(TimeSpan deadline, Exception innerException)
    : TimeoutException($"The AI endpoint remained rate limited beyond the retry deadline of {deadline}.", innerException);
