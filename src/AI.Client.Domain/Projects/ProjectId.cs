using AI.Client.Domain.Common;

namespace AI.Client.Domain.Projects;

public readonly record struct ProjectId
{
    public ProjectId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("Project ID cannot be empty.");
        }

        Value = value;
    }

    public Guid Value { get; }

    public override string ToString() => Value.ToString("D");
}
