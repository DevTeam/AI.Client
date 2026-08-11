using AI.Client.Domain.Common;

namespace AI.Client.Domain.Projects;

public readonly record struct EndpointProfileId
{
    public EndpointProfileId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("Endpoint profile ID cannot be empty.");
        }

        Value = value;
    }

    public Guid Value { get; }
}
