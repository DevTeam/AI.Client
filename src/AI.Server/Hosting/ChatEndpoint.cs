namespace AI.Server.Hosting;

using Application.Chat;
using Application.Settings;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

public interface IChatEndpoint
{
    Task<IResult> HandleAsync(ChatCompletionRequest request, CancellationToken cancellationToken);

    Task HandleStreamAsync(ChatCompletionRequest request, HttpResponse response, CancellationToken cancellationToken);
}

public sealed class ChatEndpoint(
    IChatCompletionClient client,
    IGlobalSecretStore globalSecretStore,
    ILogger<ChatEndpoint> logger) : IChatEndpoint
{
    private static readonly Action<ILogger, Guid, string, Guid?, Exception?> StreamStarted =
        LoggerMessage.Define<Guid, string, Guid?>(LogLevel.Information, new EventId(1001, "ChatStreamStarted"),
            "ChatStreamStarted OperationId={OperationId} Model={Model} CredentialProfileId={CredentialProfileId}");
    private static readonly Action<ILogger, Guid, int, int, Exception?> ChunkReceived =
        LoggerMessage.Define<Guid, int, int>(LogLevel.Information, new EventId(1002, "ContentChunkReceived"),
            "ContentChunkReceived OperationId={OperationId} ChunkIndex={ChunkIndex} ContentLength={ContentLength}");
    private static readonly Action<ILogger, Guid, int, double, Exception?> StreamCompleted =
        LoggerMessage.Define<Guid, int, double>(LogLevel.Information, new EventId(1003, "ChatStreamCompleted"),
            "ChatStreamCompleted OperationId={OperationId} ChunkCount={ChunkCount} ElapsedMs={ElapsedMs}");
    private static readonly Action<ILogger, Guid, int, double, Exception?> StreamCancelled =
        LoggerMessage.Define<Guid, int, double>(LogLevel.Warning, new EventId(1004, "ChatStreamCancelled"),
            "ChatStreamCancelled OperationId={OperationId} ChunkCount={ChunkCount} ElapsedMs={ElapsedMs}");
    private static readonly Action<ILogger, Guid, int, double, Exception?> StreamFailed =
        LoggerMessage.Define<Guid, int, double>(LogLevel.Error, new EventId(1005, "ChatStreamFailed"),
            "ChatStreamFailed OperationId={OperationId} ChunkCount={ChunkCount} ElapsedMs={ElapsedMs}");
    public async Task<IResult> HandleAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.CredentialProfileId is { } profileId && string.IsNullOrWhiteSpace(request.ApiKey))
            {
                request = request with
                {
                    ApiKey = await GetCredentialAsync(profileId, cancellationToken),
                    CredentialProfileId = null
                };
            }

            return Results.Ok(await client.CompleteAsync(request, cancellationToken));
        }
        catch (ArgumentException error)
        {
            return Results.BadRequest(new { error = error.Message });
        }
        catch (HttpRequestException)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
        catch (InvalidOperationException)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    public async Task HandleStreamAsync(
        ChatCompletionRequest request,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var operationId = Guid.CreateVersion7();
        var startedAt = Stopwatch.GetTimestamp();
        StreamStarted(logger, operationId, request.Model, request.CredentialProfileId, null);
        var chunkIndex = 0;
        try
        {
            request = await ResolveCredentialAsync(request, cancellationToken);
            response.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            await foreach (var chunk in client.StreamAsync(request, cancellationToken))
            {
                chunkIndex++;
                ChunkReceived(logger, operationId, chunkIndex, chunk.Content.Length, null);
                await response.WriteAsync($"data: {System.Text.Json.JsonSerializer.Serialize(chunk)}\n\n", cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
            }

            await response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            StreamCompleted(logger, operationId, chunkIndex, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StreamCancelled(logger, operationId, chunkIndex, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, null);
            throw;
        }
        catch (Exception error)
        {
            StreamFailed(logger, operationId, chunkIndex, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, error);
            throw;
        }
    }

    private async Task<ChatCompletionRequest> ResolveCredentialAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CredentialProfileId is not { } profileId || !string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return request;
        }

        return request with
        {
            ApiKey = await GetCredentialAsync(profileId, cancellationToken),
            CredentialProfileId = null
        };
    }

    private async Task<string?> GetCredentialAsync(Guid id, CancellationToken cancellationToken) =>
        await globalSecretStore.GetAsync("connection", id, cancellationToken);
}
