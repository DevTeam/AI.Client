namespace AI.Application.Users;

using Domain.Users;

/// <summary>Runs code for a user. Only entry points enter a scope: requests and background loops.</summary>
public interface IUserScope
{
    /// <summary>
    /// Makes <paramref name="user"/> the current user of this asynchronous flow until the result is
    /// disposed, which restores the user that was current before. Scopes nest.
    /// </summary>
    IDisposable Enter(UserId user);
}
