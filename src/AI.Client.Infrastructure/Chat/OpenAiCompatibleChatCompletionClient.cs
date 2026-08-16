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
                .Select(item => new { role = item.Role, content = item.Content })
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
            throw new HttpRequestException(
                $"The AI endpoint returned {(int)response.StatusCode} ({response.ReasonPhrase}).");
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
            throw new HttpRequestException(
                $"The AI endpoint returned {(int)response.StatusCode} ({response.ReasonPhrase}).");
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
            Content = JsonContent.Create(new
            {
                model = request.Model.Trim(),
                messages = (request.ContextMessages is { Count: > 0 }
                    ? request.ContextMessages
                    : [new ChatCompletionMessage("user", request.Message.Trim())])
                    .Select(item => new { role = item.Role, content = item.Content })
                    .ToArray(),
                stream
            })
        };
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey.Trim());
        }

        return message;
    }
}
