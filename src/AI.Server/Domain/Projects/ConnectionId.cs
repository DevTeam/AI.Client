namespace AI.Domain.Projects;

using Common;

public readonly record struct ConnectionId
{
    public ConnectionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("Endpoint profile ID cannot be empty.");
        }

        Value = value;
    }

    public Guid Value { get; }
}
