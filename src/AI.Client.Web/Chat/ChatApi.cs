using AI.Client.Contracts.Chat;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AI.Client.Web.Chat;

public sealed class ChatApi(HttpClient httpClient) : IChatApi
{
    public async Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("api/chat/completions", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Chat completion response is empty.");
    }

    public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/chat/completions/stream")
        {
            Content = JsonContent.Create(request)
        };
        using var response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].TrimStart();
            if (data == "[DONE]") yield break;
            var chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(data);
            if (chunk is not null) yield return chunk;
        }
    }
}
