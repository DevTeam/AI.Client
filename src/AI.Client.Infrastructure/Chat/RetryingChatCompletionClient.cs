namespace AI.Client.Infrastructure.Chat;

using AI.Client.Application.Chat;
using AI.Client.Contracts.Chat;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

/// <summary>
/// Waits out the endpoint instead of failing the run. A rate limit is not a decision the model
/// made and not a mistake the person watching can correct — the only useful response to one is to
/// ask again later, which is exactly what they would do by hand after the run had already been
/// thrown away. Sitting here rather than in the agent keeps the waiting invisible to everything
/// above: the loop never learns there was a pause, so a run that hits a limit stays a run in
/// progress rather than a failure that recovers.
/// </summary>
public sealed class RetryingChatCompletionClient(
    IChatCompletionClient inner,
    ILogger<RetryingChatCompletionClient> logger) : IChatCompletionClient
{
    /// <summary>
    /// Anthropic's code for "overloaded", absent from <see cref="HttpStatusCode"/> because it is
    /// not in any RFC.
    /// </summary>
    private const int Overloaded = 529;

    /// <summary>
    /// How many times a server fault or a broken connection is retried before the run is failed.
    /// Unlike a rate limit, these carry no promise that waiting changes anything: a misconfigured
    /// gateway answers 502 forever, and retrying forever would hang the run behind a spinner with
    /// nobody told why. A rate limit gets no such ceiling — it always ends.
    /// </summary>
    private const int MaxServerFaults = 5;

    private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    private static readonly Action<ILogger, string, int, int, double, Exception?> Retrying =
        LoggerMessage.Define<string, int, int, double>(LogLevel.Warning, new EventId(1101, "ChatEndpointRetrying"),
            "ChatEndpointRetrying Operation={Operation} Status={Status} Attempt={Attempt} DelaySeconds={DelaySeconds}");

    public async Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await inner.CompleteAsync(request, cancellationToken);
            }
            catch (Exception error) when (Delay(error, attempt) is { } wait)
            {
                await WaitAsync("Complete", error, attempt, wait, cancellationToken);
            }
        }
    }

    public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            // Only a stream that has not yielded anything yet may be started over. Once a chunk has
            // reached the caller it has reached the screen and the answer being assembled from it,
            // and a second attempt would repeat that text rather than replace it — so a failure
            // after the first chunk is the caller's to handle, exactly as it was before.
            var started = false;
            var enumerator = inner.StreamAsync(request, cancellationToken).GetAsyncEnumerator(cancellationToken);
            TimeSpan? wait = null;
            Exception? failure = null;
            try
            {
                while (true)
                {
                    ChatCompletionChunk chunk;
                    try
                    {
                        if (!await enumerator.MoveNextAsync()) yield break;
                        chunk = enumerator.Current;
                    }
                    catch (Exception error)
                    {
                        failure = error;
                        wait = started ? null : Delay(error, attempt);
                        break;
                    }

                    started = true;
                    yield return chunk;
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }

            if (wait is not { } delay)
            {
                ExceptionDispatchInfo.Capture(failure!).Throw();
                yield break;
            }

            await WaitAsync("Stream", failure!, attempt, delay, cancellationToken);
        }
    }

    private async Task WaitAsync(string operation, Exception error, int attempt, TimeSpan delay, CancellationToken cancellationToken)
    {
        Retrying(logger, operation, Status(error) ?? 0, attempt, delay.TotalSeconds, error);
        await Task.Delay(delay, cancellationToken);
    }

    /// <summary>
    /// How long to wait before asking again, or null when asking again cannot help. Null covers
    /// everything the endpoint has already decided — a bad key, a malformed request, an exhausted
    /// quota — and the run's own cancellation, which is a person pressing stop.
    /// </summary>
    private static TimeSpan? Delay(Exception error, int attempt) => error switch
    {
        OperationCanceledException => null,
        // An endpoint that says when to come back is believed, whatever it is busy with.
        ChatEndpointException limit when RateLimited(limit) => limit.RetryAfter ?? Backoff(attempt),
        ChatEndpointException limit when ServerFault(limit) && attempt <= MaxServerFaults =>
            limit.RetryAfter ?? Backoff(attempt),
        ChatEndpointException => null,
        // No status at all means the request never got an answer: DNS, a refused connection, a
        // reset mid-handshake. The endpoint has not refused anything, so this is worth repeating.
        HttpRequestException { StatusCode: null } when attempt <= MaxServerFaults => Backoff(attempt),
        TimeoutException or IOException when attempt <= MaxServerFaults => Backoff(attempt),
        _ => null
    };

    /// <summary>
    /// The endpoint is not refusing the work, only the timing: it is busy, throttling, or queueing.
    /// Always worth waiting out, because the condition is by definition temporary.
    /// </summary>
    private static bool RateLimited(ChatEndpointException error) =>
        error.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
        || (int?)error.StatusCode == Overloaded;

    /// <summary>
    /// The endpoint broke. Usually once; occasionally forever, which is why these attempts are counted.
    /// </summary>
    private static bool ServerFault(ChatEndpointException error) =>
        error.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout;

    /// <summary>
    /// Doubling, capped, and jittered over the lower half of the interval: several runs that were
    /// rate-limited by the same account together must not come back together and limit each other
    /// again, and without the jitter the doubling alone keeps them in lockstep.
    /// </summary>
    private static TimeSpan Backoff(int attempt)
    {
        var doubled = FirstBackoff * Math.Pow(2, Math.Min(attempt - 1, 10));
        var capped = doubled < MaxBackoff ? doubled : MaxBackoff;
        return capped * (0.5 + Random.Shared.NextDouble() * 0.5);
    }

    private static int? Status(Exception error) => error is HttpRequestException { StatusCode: { } code } ? (int)code : null;
}
