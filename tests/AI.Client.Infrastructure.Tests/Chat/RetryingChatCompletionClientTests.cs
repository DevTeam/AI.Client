namespace AI.Client.Infrastructure.Tests.Chat;

using AI.Client.Application.Chat;
using AI.Client.Contracts.Chat;
using AI.Client.Infrastructure.Chat;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using System.Net;
using System.Runtime.CompilerServices;
using Xunit;

public class RetryingChatCompletionClientTests
{
    private static readonly ChatCompletionRequest Request = new("https://llm.example/v1", "test-model", null, "Hi");

    [Fact]
    public async Task ShouldWaitOutARateLimitAndDeliverTheAnswer()
    {
        var endpoint = new Endpoint(
            RateLimit(HttpStatusCode.TooManyRequests),
            RateLimit(HttpStatusCode.TooManyRequests),
            null);

        var chunks = await CollectAsync(CreateInstance(endpoint));

        chunks.ShouldHaveSingleItem().Content.ShouldBe("Hello");
        endpoint.Attempts.ShouldBe(3);
    }

    [Fact]
    public async Task ShouldWaitOutAnOverloadedEndpoint()
    {
        var endpoint = new Endpoint(RateLimit((HttpStatusCode)529), null);

        await CollectAsync(CreateInstance(endpoint));

        endpoint.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task ShouldStopRetryingServerFaultsSoARunCannotHangForever()
    {
        var faults = Enumerable.Repeat(RateLimit(HttpStatusCode.BadGateway), 20).ToArray();
        var endpoint = new Endpoint(faults);

        await Should.ThrowAsync<ChatEndpointException>(async () => await CollectAsync(CreateInstance(endpoint)));

        // Five retries after the first attempt, then the run is allowed to fail.
        endpoint.Attempts.ShouldBe(6);
    }

    [Fact]
    public async Task ShouldNotRetryWhatTheEndpointHasAlreadyDecided()
    {
        var endpoint = new Endpoint(RateLimit(HttpStatusCode.Unauthorized), null);

        await Should.ThrowAsync<ChatEndpointException>(async () => await CollectAsync(CreateInstance(endpoint)));

        endpoint.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task ShouldNotRepeatAnAnswerThatHasAlreadyStartedArriving()
    {
        // Text that reached the caller has reached the screen; asking again would send it twice.
        var endpoint = new Endpoint(RateLimit(HttpStatusCode.TooManyRequests)) { ChunksBeforeFailure = 1 };

        var chunks = new List<ChatCompletionChunk>();
        await Should.ThrowAsync<ChatEndpointException>(async () =>
        {
            await foreach (var chunk in CreateInstance(endpoint).StreamAsync(Request, CancellationToken.None))
                chunks.Add(chunk);
        });

        chunks.ShouldHaveSingleItem();
        endpoint.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task ShouldLeaveCancellationAlone()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var endpoint = new Endpoint();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in CreateInstance(endpoint).StreamAsync(Request, source.Token)) { }
        });
    }

    [Fact]
    public async Task ShouldWaitOutARateLimitOnANonStreamingCompletion()
    {
        var endpoint = new Endpoint(RateLimit(HttpStatusCode.TooManyRequests), null);

        var response = await CreateInstance(endpoint).CompleteAsync(Request, CancellationToken.None);

        response.Content.ShouldBe("Hello");
        endpoint.Attempts.ShouldBe(2);
    }

    private static RetryingChatCompletionClient CreateInstance(IChatCompletionClient endpoint) =>
        new(endpoint, NullLogger<RetryingChatCompletionClient>.Instance);

    // Zero, so the tests measure the decision to retry rather than the wait it schedules.
    private static ChatEndpointException RateLimit(HttpStatusCode status) =>
        new($"The AI endpoint returned {(int)status}.", status, TimeSpan.Zero);

    private static async Task<List<ChatCompletionChunk>> CollectAsync(RetryingChatCompletionClient client)
    {
        var chunks = new List<ChatCompletionChunk>();
        await foreach (var chunk in client.StreamAsync(Request, CancellationToken.None)) chunks.Add(chunk);
        return chunks;
    }

    /// <summary>
    /// Fails each call with the next exception in the list, or answers when that entry is null.
    /// A list shorter than the number of attempts keeps failing with its last entry.
    /// </summary>
    private sealed class Endpoint(params Exception?[] failures) : IChatCompletionClient
    {
        public int Attempts { get; private set; }

        /// <summary>How many chunks arrive before the failure, for the mid-stream case.</summary>
        public int ChunksBeforeFailure { get; init; }

        public Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Next() is { } failure
                ? Task.FromException<ChatCompletionResponse>(failure)
                : Task.FromResult(new ChatCompletionResponse("Hello", "test-model"));
        }

        public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
            ChatCompletionRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var failure = Next();
            for (var index = 0; failure is not null && index < ChunksBeforeFailure; index++)
            {
                yield return new ChatCompletionChunk("Par");
            }

            if (failure is not null) throw failure;
            await Task.Yield();
            yield return new ChatCompletionChunk("Hello", "test-model", null, "stop");
        }

        private Exception? Next()
        {
            var failure = failures.Length == 0 ? null : failures[Math.Min(Attempts, failures.Length - 1)];
            Attempts++;
            return failure;
        }
    }
}
