namespace AI.Domain.Users;

using Common;

/// <summary>The person stored data and running operations belong to (ADR-014).</summary>
public readonly record struct UserId
{
    /// <summary>The only user of a Host that has no sign-in: the person at this computer.</summary>
    public static readonly UserId Local = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));

    public UserId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("User ID cannot be empty.");
        }

        Value = value;
    }

    public Guid Value { get; }

    public override string ToString() => Value.ToString("D");
}
