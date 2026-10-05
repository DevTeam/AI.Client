namespace AI.Infrastructure.Chat;

using AI.Application.Chat;
using AI.Application.Projects;
using AI.Application.Resources;
using AI.Application.Usage;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

public sealed class OpenAiCompatibleChatCompletionClient(
    HttpClient httpClient,
    IChatCompletionSseParser sseParser,
    IChatTransportPolicy policy,
    IChatCompletionUsageReader usageReader,
    IRateLimitHeaderReader rateLimitReader,
    IConnectionRateLimits rateLimits,
    IClock clock,
    IResourceAssetService images) : IChatCompletionClient
{
    // Endpoints that refused stream_options. Usage is asked for by default because nearly every
    // OpenAI-compatible server accepts it; the few strict ones that reject unknown fields are
    // remembered for the life of the process and streamed without it.
    private readonly ConcurrentDictionary<string, bool> _withoutStreamUsage = new(StringComparer.OrdinalIgnoreCase);

    // Providers routinely put the actual reason (e.g. "context length exceeded", a validation
    // complaint about a malformed tool_calls entry) in the response body, not the status line —
    // a bare "400 (Bad Request)" is not enough to diagnose or even reproduce the failure after
    // the fact. Bounded so a large HTML error page from a misconfigured proxy doesn't get fully
    // quoted into logs and chat history.
    private const int ErrorBodyPreviewCharacters = 2000;

    public async Task<ChatCompletionResponse> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Uri.TryCreate(request.BaseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Base URL must be an absolute HTTP or HTTPS URL.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new ArgumentException("Model cannot be empty.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Message) && request.ContextMessages is not { Count: > 0 })
        {
            throw new ArgumentException("Message cannot be empty.", nameof(request));
        }

        var endpoint = new Uri(baseUri.ToString().TrimEnd('/') + "/chat/completions");
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        message.Content = JsonContent.Create(await CreateBodyAsync(request, false, false, cancellationToken));
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey.Trim());
        }

        using var response = await SendAsync(message, request.CredentialProfileId, cancellationToken);
        var body = await ReadBodyAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw Failure(response, body);
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("The AI endpoint returned an empty completion.");
        }

        var model = root.TryGetProperty("model", out var modelElement)
            ? modelElement.GetString() ?? request.Model.Trim()
            : request.Model.Trim();
        return new ChatCompletionResponse(content, model, usageReader.Read(root));
    }

    public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
        ChatCompletionRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var endpointKey = request.BaseUrl.Trim().TrimEnd('/');
        var askUsage = !_withoutStreamUsage.ContainsKey(endpointKey);
        using var message = await CreateRequestAsync(request, true, askUsage, cancellationToken);
        var response = await SendAsync(message, request.CredentialProfileId, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await ReadBodyAsync(response, cancellationToken);
            if (!askUsage || !RejectsStreamOptions(response, errorBody))
            {
                using (response) throw Failure(response, errorBody);
            }

            response.Dispose();
            _withoutStreamUsage[endpointKey] = true;
            using var retry = await CreateRequestAsync(request, true, false, cancellationToken);
            response = await SendAsync(retry, request.CredentialProfileId, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var retryBody = await ReadBodyAsync(response, cancellationToken);
                using (response) throw Failure(response, retryBody);
            }
        }

        using var _ = response;

        using var firstToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        firstToken.CancelAfter(policy.FirstTokenTimeout);
        var started = false;
        await using var stream = await ReadStreamAsync(response, firstToken.Token, cancellationToken);
        await using var chunks = sseParser.ParseAsync(stream, firstToken.Token).GetAsyncEnumerator(firstToken.Token);
        while (true)
        {
            ChatCompletionChunk chunk;
            try
            {
                if (!await chunks.MoveNextAsync()) yield break;
                chunk = chunks.Current;
            }
            catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested && !started)
            {
                throw new ChatFirstTokenTimeoutException(policy.FirstTokenTimeout, error);
            }

            if (!started)
            {
                started = true;
                firstToken.CancelAfter(Timeout.InfiniteTimeSpan);
            }
            yield return chunk;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, Guid? connectionId,
        CancellationToken cancellationToken)
    {
        using var headers = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        headers.CancelAfter(policy.ResponseHeadersTimeout);
        try
        {
            var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, headers.Token);
            // A refusal states its limits as much as an answer does, and is when they matter most.
            if (connectionId is { } connection && rateLimitReader.Read(response.Headers, clock.UtcNow) is { } limits)
                rateLimits.Record(connection, limits);
            return response;
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatResponseHeadersTimeoutException(policy.ResponseHeadersTimeout, error);
        }
    }

    private async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var body = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        body.CancelAfter(policy.FirstTokenTimeout);
        try
        {
            return await response.Content.ReadAsStringAsync(body.Token);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatFirstTokenTimeoutException(policy.FirstTokenTimeout, error);
        }
    }

    private async Task<Stream> ReadStreamAsync(HttpResponseMessage response, CancellationToken firstToken,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStreamAsync(firstToken);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatFirstTokenTimeoutException(policy.FirstTokenTimeout, error);
        }
    }

    /// <summary>
    /// Whether a refusal is about <c>stream_options</c> rather than anything else in the request:
    /// only then is sending it again without the field worth one more request.
    /// </summary>
    private static bool RejectsStreamOptions(HttpResponseMessage response, string body) =>
        response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
        && (body.Contains("stream_options", StringComparison.OrdinalIgnoreCase)
            || body.Contains("include_usage", StringComparison.OrdinalIgnoreCase));

    private async Task<HttpRequestMessage> CreateRequestAsync(ChatCompletionRequest request, bool stream, bool askUsage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Uri.TryCreate(request.BaseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Base URL must be an absolute HTTP or HTTPS URL.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Model) ||
            string.IsNullOrWhiteSpace(request.Message) && request.ContextMessages is not { Count: > 0 })
        {
            throw new ArgumentException("Model and message cannot be empty.", nameof(request));
        }

        var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(baseUri.ToString().TrimEnd('/') + "/chat/completions"))
        {
            Content = JsonContent.Create(await CreateBodyAsync(request, stream, askUsage, cancellationToken))
        };
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey.Trim());
        }

        return message;
    }
    // What the endpoint refused with, kept whole: the text for the person reading it, the status
    // code and Retry-After for whoever has to decide whether asking again could ever work.
    private static ChatEndpointException Failure(HttpResponseMessage response, string body) =>
        new(FormatErrorMessage(response, body), response.StatusCode, RetryAfter(response));

    // Retry-After comes either as a number of seconds or as an absolute date; a date already in the
    // past means "now", and a clock skewed the other way must not turn into a negative delay.
    private static TimeSpan? RetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter switch
    {
        { Delta: { } delta } => delta > TimeSpan.Zero ? delta : TimeSpan.Zero,
        { Date: { } date } => date - DateTimeOffset.UtcNow is { Ticks: > 0 } wait ? wait : TimeSpan.Zero,
        _ => null
    };

    // Trims to one line so a multi-line HTML/JSON error body doesn't blow up log formatting, and
    // truncates it rather than the whole thing so this message stays safe to surface in the UI
    // and to persist in chat/run history.
    private static string FormatErrorMessage(HttpResponseMessage response, string body)
    {
        var prefix = $"The AI endpoint returned {(int)response.StatusCode} ({response.ReasonPhrase}).";
        var trimmed = body.Trim();
        if (trimmed.Length == 0) return prefix;

        var oneLine = trimmed.ReplaceLineEndings(" ");
        var preview = oneLine.Length > ErrorBodyPreviewCharacters
            ? oneLine[..ErrorBodyPreviewCharacters] + "…"
            : oneLine;
        return $"{prefix} {preview}";
    }

    private async Task<Dictionary<string, object?>> CreateBodyAsync(ChatCompletionRequest request, bool stream, bool askUsage,
        CancellationToken cancellationToken)
    {
        var messages = new List<Dictionary<string, object?>>();
        foreach (var item in request.ContextMessages is { Count: > 0 } ? request.ContextMessages
                     : [new ChatCompletionMessage("user", request.Message.Trim())])
        {
            var message = new Dictionary<string, object?> { ["role"] = item.Role, ["content"] = item.ForModel };
            if (item.ImageAssetIds is { Count: > 0 })
            {
                if (request.ProjectId is not { } projectId)
                    throw new InvalidOperationException("Project is required for image input.");
                if (item.Role != "user") throw new InvalidOperationException("Only user messages may contain image input.");
                var content = new List<object> { new { type = "text", text = item.ForModel } };
                foreach (var assetId in item.ImageAssetIds)
                {
                    var image = await images.ReadAsync(projectId, assetId, cancellationToken)
                        ?? throw new FileNotFoundException("An attached image is unavailable.");
                    content.Add(new { type = "image_url", image_url = new { url =
                        $"data:{image.MediaType};base64,{Convert.ToBase64String(image.Data)}" } });
                }
                message["content"] = content;
            }
            if (item.ToolCallId is not null) message["tool_call_id"] = item.ToolCallId;
            if (item.ToolCalls is { Count: > 0 }) message["tool_calls"] = item.ToolCalls.Select(call => new
            {
                id = call.Id, type = "function", function = new { name = call.Name, arguments = call.Arguments }
            }).ToArray();
            messages.Add(message);
        }
        var body = new Dictionary<string, object?> { ["model"] = request.Model.Trim(), ["messages"] = messages, ["stream"] = stream };
        // Without it a stream says nothing about what it used: the report is an extra final chunk
        // that endpoints send only when asked.
        if (stream && askUsage) body["stream_options"] = new { include_usage = true };
        if (request.Tools is { Count: > 0 }) body["tools"] = request.Tools.Select(tool => new
        {
            type = "function", function = new { name = tool.Name, description = tool.Description, parameters = tool.InputSchema }
        }).ToArray();
        return body;
    }

}
