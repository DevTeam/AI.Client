namespace AI.Application.Users;

using Domain.Users;

/// <summary>A Host without sign-in has exactly one user.</summary>
public sealed class LocalUserDirectory : IUserDirectory
{
    public Task<IReadOnlyList<UserId>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UserId>>([UserId.Local]);
}
