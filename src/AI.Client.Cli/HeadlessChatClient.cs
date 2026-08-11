using AI.Client.Contracts.Chat;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AI.Client.Cli;

internal interface IHeadlessChatClient
{
    Task<IReadOnlyList<ProjectDto>> GetProjectsAsync(Uri host, CancellationToken cancellationToken);
    Task<ProjectDetailsDto?> GetProjectAsync(Uri host, Guid id, CancellationToken cancellationToken);
    IAsyncEnumerable<ChatCompletionChunk> StreamAsync(Uri host, ChatCompletionRequest request, CancellationToken cancellationToken);
}

internal sealed class HeadlessChatClient : IHeadlessChatClient
{
    public async Task<IReadOnlyList<ProjectDto>> GetProjectsAsync(Uri host, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = host };
        return await client.GetFromJsonAsync<ProjectDto[]>("api/projects", cancellationToken) ?? [];
    }

    public async Task<ProjectDetailsDto?> GetProjectAsync(Uri host, Guid id, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = host };
        return await client.GetFromJsonAsync<ProjectDetailsDto>($"api/projects/{id}", cancellationToken);
    }

    public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(Uri host, ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = host, Timeout = Timeout.InfiniteTimeSpan };
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/chat/completions/stream")
        {
            Content = JsonContent.Create(request)
        };
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") yield break;
            var chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(data);
            if (chunk is not null) yield return chunk;
        }
    }
}

internal sealed record ProjectDto(Guid Id, string Name);
internal sealed record ProjectDetailsDto(Guid Id, string Name, EndpointDto[] EndpointProfiles, Guid? DefaultEndpointProfileId,
    object[] McpServers, object[] ToolPolicies, object[] DirectoryGrants);
internal sealed record EndpointDto(Guid Id, string Name, string BaseUrl, string Model, bool HasCredential);
