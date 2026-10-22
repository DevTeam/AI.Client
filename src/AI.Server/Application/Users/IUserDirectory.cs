namespace AI.Application.Users;

using Domain.Users;

/// <summary>The users whose data the Host keeps, for background work that runs for each of them.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyList<UserId>> ListAsync(CancellationToken cancellationToken);
}
