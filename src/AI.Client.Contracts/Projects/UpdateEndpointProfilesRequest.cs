namespace AI.Client.Contracts.Projects;

public sealed record UpdateEndpointProfilesRequest(
    long Revision,
    IReadOnlyList<EndpointProfileSettings> Profiles,
    Guid? DefaultEndpointProfileId = null);
