namespace AI.Infrastructure.Chat;

using System.Net;

/// <summary>
/// A refusal from the AI endpoint that still carries what the refusal was. The message alone cannot
/// be acted on — deciding whether waiting will help means telling a rate limit from a bad key, and
/// the status code is the only thing that says which. Still an <see cref="HttpRequestException"/>,
/// so everything upstream that already classifies endpoint failures keeps working unchanged.
/// </summary>
public sealed class ChatEndpointException(string message, HttpStatusCode statusCode, TimeSpan? retryAfter = null)
    : HttpRequestException(message, null, statusCode)
{
    /// <summary>How long the endpoint asked to be left alone, when it said so.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
