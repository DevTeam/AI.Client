namespace AI.Client.Contracts.Projects;

public sealed record EndpointProfileSettings(Guid Id, string Name, string BaseUrl, string Model, bool HasCredential);
