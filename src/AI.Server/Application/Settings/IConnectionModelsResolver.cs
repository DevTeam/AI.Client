namespace AI.Application.Settings;

using AI.Contracts.Settings;

/// <summary>
/// Asks an OpenAI-compatible endpoint for the list of models it serves through
/// <c>GET /v1/models</c>. Lives behind an interface so the global settings service can be tested
/// without a real provider, and so future transports (for example, an Azure-specific catalog
/// endpoint) can be slotted in without touching the call site.
/// </summary>
public interface IConnectionModelsResolver
{
    Task<IReadOnlyList<ResolvedModelInfo>> ResolveAsync(
        string baseUrl,
        string? apiKey,
        CancellationToken cancellationToken);
}
