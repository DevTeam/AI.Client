namespace AI.Client.Application.Settings;

using AI.Client.Contracts.Settings;

public interface IGlobalSettingsService
{
    Task<GlobalSettings> GetAsync(CancellationToken cancellationToken);

    Task<GlobalSettings> SaveAsync(SaveGlobalSettingsRequest request, CancellationToken cancellationToken);

    Task<bool> SetConnectionCredentialAsync(Guid id, string? value, CancellationToken cancellationToken);

    Task<bool> SetMcpCredentialAsync(Guid id, string? value, CancellationToken cancellationToken);
    Task<GlobalSettings> SetToolPolicyAsync(McpToolPolicySettings policy, CancellationToken cancellationToken);
    Task<GlobalSettings> RemoveToolPolicyAsync(Guid serverId, string name, string schemaHash, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the endpoint in <paramref name="request"/> for the models it serves through
    /// <c>GET /v1/models</c>. The key typed in the request wins; otherwise the credential saved for
    /// <paramref name="connectionId"/>, if any, is sent in an <c>Authorization: Bearer</c> header.
    /// The connection does not have to be saved yet. Throws <see cref="ArgumentException"/> for an
    /// empty or malformed Base URL and <see cref="InvalidOperationException"/> when the endpoint
    /// cannot be reached, times out, refuses, or answers with something that is not JSON.
    /// </summary>
    Task<IReadOnlyList<ResolvedModelInfo>> ResolveConnectionModelsAsync(
        Guid connectionId, ResolveConnectionModelsRequest request, CancellationToken cancellationToken);
}
