using AI.Client.Domain.Projects;

namespace AI.Client.Application.Projects;

public interface IEndpointCredentialStore
{
    Task<string?> GetAsync(EndpointProfileId profileId, CancellationToken cancellationToken);

    Task SetAsync(EndpointProfileId profileId, string? apiKey, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(EndpointProfileId profileId, CancellationToken cancellationToken);
}
