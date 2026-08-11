using AI.Client.Domain.Projects;

namespace AI.Client.Infrastructure.Credentials;

public sealed class EndpointCredentialPaths(string rootDirectory) : IEndpointCredentialPaths
{
    public string GetPath(EndpointProfileId profileId) =>
        Path.Combine(rootDirectory, "credentials", $"{profileId.Value:N}.key");
}
