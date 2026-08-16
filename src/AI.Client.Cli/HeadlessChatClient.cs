namespace AI.Client.Cli;

using Contracts.Chats;
using Contracts.Projects;
using Contracts.Settings;
using Contracts.Runs;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

internal interface IHeadlessChatClient
{
    Task<IReadOnlyList<ProjectSummary>> GetProjectsAsync(Uri host, CancellationToken cancellationToken);
    Task<ProjectDetails> GetProjectAsync(Uri host, Guid id, CancellationToken cancellationToken);
    Task<GlobalSettings> GetSettingsAsync(Uri host, CancellationToken cancellationToken);
    Task<ChatDetails> CreateChatAsync(Uri host, Guid projectId, CreateChatRequest request, CancellationToken cancellationToken);
    Task<ChatDetails> GetChatAsync(Uri host, Guid projectId, Guid chatId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot> SubmitAsync(Uri host, Guid projectId, Guid chatId, SubmitChatMessageRequest request, CancellationToken cancellationToken);
    Task StopAsync(Uri host, Guid projectId, Guid chatId, CancellationToken cancellationToken);
    Task DeleteChatAsync(Uri host, Guid projectId, Guid chatId, long revision, CancellationToken cancellationToken);
    IAsyncEnumerable<IReadOnlyList<ChatRunSnapshot>> WatchAsync(Uri host, CancellationToken cancellationToken);
}

internal sealed class HeadlessChatClient(HttpClient client) : IHeadlessChatClient
{
    public async Task<IReadOnlyList<ProjectSummary>> GetProjectsAsync(Uri host, CancellationToken cancellationToken) =>
        await GetAsync<ProjectSummary[]>(host, "api/projects", cancellationToken);
    public Task<ProjectDetails> GetProjectAsync(Uri host, Guid id, CancellationToken cancellationToken) =>
        GetAsync<ProjectDetails>(host, $"api/projects/{id}", cancellationToken);
    public Task<GlobalSettings> GetSettingsAsync(Uri host, CancellationToken cancellationToken) =>
        GetAsync<GlobalSettings>(host, "api/settings", cancellationToken);
    public Task<ChatDetails> CreateChatAsync(Uri host, Guid projectId, CreateChatRequest request, CancellationToken cancellationToken) =>
        PostAsync<ChatDetails>(host, $"api/projects/{projectId}/chats", request, cancellationToken);
    public Task<ChatDetails> GetChatAsync(Uri host, Guid projectId, Guid chatId, CancellationToken cancellationToken) =>
        GetAsync<ChatDetails>(host, $"api/projects/{projectId}/chats/{chatId}", cancellationToken);
    public Task<ChatRunSnapshot> SubmitAsync(Uri host, Guid projectId, Guid chatId, SubmitChatMessageRequest request, CancellationToken cancellationToken) =>
        PostAsync<ChatRunSnapshot>(host, $"api/projects/{projectId}/chats/{chatId}/submit", request, cancellationToken);

    public async Task StopAsync(Uri host, Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(new Uri(host, $"api/projects/{projectId}/chats/{chatId}/stop?branchId={chatId}&operationId={Guid.CreateVersion7()}"), null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
    public async Task DeleteChatAsync(Uri host, Guid projectId, Guid chatId, long revision, CancellationToken cancellationToken)
    {
        using var response = await client.DeleteAsync(new Uri(host, $"api/projects/{projectId}/chats/{chatId}?revision={revision}"), cancellationToken);
        response.EnsureSuccessStatusCode();
    }
    private async Task<T> GetAsync<T>(Uri host, string path, CancellationToken token) =>
        await client.GetFromJsonAsync<T>(new Uri(host, path), token) ?? throw new InvalidOperationException("Empty response.");
    private async Task<T> PostAsync<T>(Uri host, string path, object request, CancellationToken token)
    {
        using var response = await client.PostAsJsonAsync(new Uri(host, path), request, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(token) ?? throw new InvalidOperationException("Empty response.");
    }
    public async IAsyncEnumerable<IReadOnlyList<ChatRunSnapshot>> WatchAsync(Uri host,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri(host, "api/runs/events"), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            yield return JsonSerializer.Deserialize<ChatRunSnapshot[]>(line[5..]) ?? [];
        }
    }
}
