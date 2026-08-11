using AI.Client.Domain.Common;

namespace AI.Client.Domain.Projects;

public sealed class EndpointProfile(EndpointProfileId id, string name, string baseUrl, string model)
{
    public EndpointProfileId Id { get; } = id;

    public string Name { get; } = string.IsNullOrWhiteSpace(name)
        ? throw new DomainException("Endpoint profile name cannot be empty.")
        : name.Trim();

    public string BaseUrl { get; } = Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
            ? uri.ToString().TrimEnd('/')
            : throw new DomainException("Endpoint profile base URL must be an absolute HTTP or HTTPS URL.");

    public string Model { get; } = string.IsNullOrWhiteSpace(model)
        ? throw new DomainException("Endpoint profile model cannot be empty.")
        : model.Trim();
}
