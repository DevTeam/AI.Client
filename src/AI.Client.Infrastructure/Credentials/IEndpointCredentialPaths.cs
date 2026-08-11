using AI.Client.Domain.Projects;

namespace AI.Client.Infrastructure.Credentials;

public interface IEndpointCredentialPaths
{
    string GetPath(EndpointProfileId profileId);
}
