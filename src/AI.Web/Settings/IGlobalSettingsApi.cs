namespace AI.Web.Settings;

using AI.Contracts.Settings;

public interface IGlobalSettingsApi
{
    Task<IReadOnlyList<McpToolInfo>> GetDefaultToolsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<McpToolInfo>> DiscoverMcpToolsAsync(DiscoverMcpToolsRequest request, CancellationToken cancellationToken);
    Task<GlobalSettings> GetAsync(CancellationToken cancellationToken);

    Task<GlobalSettings> SaveAsync(SaveGlobalSettingsRequest request, CancellationToken cancellationToken);

    Task SetConnectionCredentialAsync(Guid id, string? value, CancellationToken cancellationToken);

    Task SetMcpCredentialAsync(Guid id, string? value, CancellationToken cancellationToken);
    Task<GlobalSettings> RemoveToolPolicyAsync(Guid serverId, string name, string schemaHash, CancellationToken cancellationToken);
    Task<GlobalSettings> SetChatAutomationAsync(ChatAutomationSettings automation, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the endpoint typed into the connection editor for the models it serves. Returns an
    /// empty array when the endpoint answers with no models; throws
    /// <see cref="HttpRequestException"/> when the Host itself is unreachable and
    /// <see cref="InvalidOperationException"/> with the Host's one-line explanation otherwise
    /// (bad URL, endpoint unreachable, timeout, error status with a body preview).
    /// </summary>
    Task<IReadOnlyList<ResolvedModelInfo>> GetConnectionModelsAsync(
        Guid id, ResolveConnectionModelsRequest request, CancellationToken cancellationToken);
}
