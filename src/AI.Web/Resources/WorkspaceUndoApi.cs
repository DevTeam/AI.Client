namespace AI.Web.Resources;

using System.Net;
using System.Net.Http.Json;
using AI.Contracts.Workspace;

public sealed class WorkspaceUndoApi(HttpClient http) : IWorkspaceUndoApi
{
    public async Task<WorkspaceUndoStatus?> GetAsync(Guid projectId, Guid chatId, Guid undoId,
        CancellationToken token)
    {
        using var response = await http.GetAsync($"api/projects/{projectId}/chats/{chatId}/workspace-undo/status/{undoId}", token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, token);
        return await response.Content.ReadFromJsonAsync<WorkspaceUndoStatus>(token);
    }

    public async Task<WorkspaceUndoStatus?> UndoAsync(Guid projectId, Guid chatId, Guid messageId, string? path,
        CancellationToken token)
    {
        using var response = await http.PostAsJsonAsync(PathFor(projectId, chatId, messageId),
            new WorkspaceUndoRequest(path), token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, token);
        return await response.Content.ReadFromJsonAsync<WorkspaceUndoStatus>(token);
    }

    private static string PathFor(Guid projectId, Guid chatId, Guid messageId) =>
        $"api/projects/{projectId}/chats/{chatId}/workspace-undo/{messageId}";

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(token);
        throw new InvalidOperationException($"Undo request failed ({(int)response.StatusCode}): {body[..Math.Min(body.Length, 600)]}");
    }
}
