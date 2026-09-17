namespace AI.Client.Infrastructure.Chat;

using AI.Client.Application.Chat;
using AI.Client.Contracts.Chat;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

public sealed class OpenAiCompatibleChatCompletionClient(
    HttpClient httpClient,
    IChatCompletionSseParser sseParser) : IChatCompletionClient
{
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

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ArgumentException("Message cannot be empty.", nameof(request));
        }

        var endpoint = new Uri(baseUri.ToString().TrimEnd('/') + "/chat/completions");
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        message.Content = JsonContent.Create(new
        {
            model = request.Model.Trim(),
            messages = (request.ContextMessages is { Count: > 0 }
                    ? request.ContextMessages
                    : [new ChatCompletionMessage("user", request.Message.Trim())])
                .Select(item => new { role = item.Role, content = item.ForModel })
                .ToArray(),
            stream = false
        });
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey.Trim());
        }

        using var response = await httpClient.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
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
        return new ChatCompletionResponse(content, model);
    }

    public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
        ChatCompletionRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var message = CreateRequest(request, true);
        using var response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw Failure(response, errorBody);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await foreach (var chunk in sseParser.ParseAsync(stream, cancellationToken))
        {
            yield return chunk;
        }
    }

    private static HttpRequestMessage CreateRequest(ChatCompletionRequest request, bool stream)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Uri.TryCreate(request.BaseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Base URL must be an absolute HTTP or HTTPS URL.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Model) || string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ArgumentException("Model and message cannot be empty.", nameof(request));
        }

        var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(baseUri.ToString().TrimEnd('/') + "/chat/completions"))
        {
            Content = JsonContent.Create(CreateBody(request, stream))
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

    private static Dictionary<string, object?> CreateBody(ChatCompletionRequest request, bool stream)
    {
        var messages = (request.ContextMessages is { Count: > 0 } ? request.ContextMessages
            : [new ChatCompletionMessage("user", request.Message.Trim())]).Select(item =>
        {
            var message = new Dictionary<string, object?> { ["role"] = item.Role, ["content"] = item.ForModel };
            if (item.ToolCallId is not null) message["tool_call_id"] = item.ToolCallId;
            if (item.ToolCalls is { Count: > 0 }) message["tool_calls"] = item.ToolCalls.Select(call => new
            {
                id = call.Id, type = "function", function = new { name = call.Name, arguments = call.Arguments }
            }).ToArray();
            return message;
        }).ToArray();
        var body = new Dictionary<string, object?> { ["model"] = request.Model.Trim(), ["messages"] = messages, ["stream"] = stream };
        if (request.Tools is { Count: > 0 }) body["tools"] = request.Tools.Select(tool => new
        {
            type = "function", function = new { name = tool.Name, description = tool.Description, parameters = tool.InputSchema }
        }).ToArray();
        return body;
    }

}
